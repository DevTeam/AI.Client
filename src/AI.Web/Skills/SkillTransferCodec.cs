namespace AI.Web.Skills;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AI.Web.Settings;

public sealed class SkillTransferCodec : ISkillTransferCodec
{
    // The limits the Host enforces when it saves a SKILL.md; checking them here names the skill
    // that breaks one in the preview, rather than failing the whole save afterwards.
    private const int MaxLength = 48_000;
    private const int MaxNameLength = 120;
    private const int MaxDescriptionLength = 1_000;
    private const int MaxIdLength = 64;
    private const string Fence = "---";
    private const string DefaultParameters = """{"type":"object","properties":{},"additionalProperties":false}""";
    private const string SkillFile = "SKILL.md";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly UTF8Encoding Utf8 = new(false);

    public SkillTransferItem ToTransfer(string content, bool enabled)
    {
        var lines = content.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n').ToList();
        var end = FrontmatterEnd(lines);
        if (end > 0)
        {
            // Revision and the switch are this Host's bookkeeping, written on every save.
            lines = [lines[0], .. lines.Take(end).Skip(1).Where(line => !IsField(line, "revision") && !IsField(line, "enabled")), .. lines.Skip(end)];
        }

        var text = string.Join('\n', lines).Trim();
        return new SkillTransferItem(
            Field(lines, "id") ?? string.Empty, Field(lines, "name") ?? string.Empty,
            Field(lines, "description") ?? string.Empty, text, enabled);
    }

    public string ExportText(IReadOnlyList<SkillTransferItem> skills)
    {
        if (skills.Count == 1) return Portable(skills[0]);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("skills");
            foreach (var (folder, skill) in Folders(skills))
            {
                writer.WriteString(folder, Portable(skill));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public byte[] ExportArchive(IReadOnlyList<SkillTransferItem> skills)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (folder, skill) in Folders(skills))
            {
                var entry = archive.CreateEntry($"{folder}/{SkillFile}", CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Utf8);
                writer.Write(Portable(skill));
                writer.Write('\n');
            }
        }

        return stream.ToArray();
    }

    public SkillTransferParseResult Parse(string text)
    {
        var trimmed = text.TrimStart('﻿').Trim();
        if (trimmed.Length == 0) return SkillTransferParseResult.Failed("Nothing to import: the text is empty.");
        var reader = new Reader();
        if (trimmed[0] is '{' or '[')
        {
            if (ReadJson(trimmed, reader) is { } error) return SkillTransferParseResult.Failed(error);
        }
        else if (trimmed.StartsWith(Fence, StringComparison.Ordinal))
        {
            reader.Read(trimmed, "Skill");
        }
        else
        {
            return SkillTransferParseResult.Failed(
                "Nothing to import: expected a SKILL.md that starts with --- frontmatter, or JSON with skills.");
        }

        return reader.Result("Nothing to import: the text lists no skills.");
    }

    public SkillTransferParseResult ParseArchive(byte[] archive)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
            var files = zip.Entries.Where(entry => entry.FullName.Length > 0 && !entry.FullName.EndsWith('/') && !IsJunk(entry.FullName)).ToArray();
            var skills = files.Where(entry => string.Equals(entry.Name, SkillFile, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal).ToArray();
            if (skills.Length == 0) return SkillTransferParseResult.Failed("Nothing to import: the archive holds no SKILL.md.");

            // Each other file belongs to the skill whose folder holds it most closely.
            var folders = skills.Select(entry => entry.FullName[..^entry.Name.Length]).ToArray();
            var leftBehind = folders.ToDictionary(folder => folder, _ => 0, StringComparer.Ordinal);
            foreach (var file in files.Except(skills))
            {
                var owner = folders.Where(folder => file.FullName.StartsWith(folder, StringComparison.Ordinal))
                    .MaxBy(folder => folder.Length);
                if (owner is not null) leftBehind[owner]++;
            }

            var reader = new Reader();
            foreach (var entry in skills)
            {
                var folder = entry.FullName[..^entry.Name.Length];
                var trimmed = folder.TrimEnd('/');
                var hint = trimmed.Length == 0 ? SkillFile : trimmed[(trimmed.LastIndexOf('/') + 1)..];
                if (entry.Length > MaxLength * 4)
                {
                    reader.Skip(hint, $"Longer than {MaxLength.ToString("N0", CultureInfo.InvariantCulture)} characters, the most a SKILL.md may hold.");
                    continue;
                }

                using var stream = new StreamReader(entry.Open(), Utf8);
                IReadOnlyList<string> notes = leftBehind[folder] switch
                {
                    0 => [],
                    1 => ["1 other file in its folder was not imported: only the SKILL.md is kept."],
                    var count => [$"{count} other files in its folder were not imported: only the SKILL.md is kept."]
                };
                reader.Read(stream.ReadToEnd(), hint, notes);
            }

            return reader.Result("Nothing to import: the archive holds no SKILL.md.");
        }
        catch (InvalidDataException)
        {
            return SkillTransferParseResult.Failed("This is not a valid .zip file.");
        }
    }

    public SkillTransferItem WithIdentity(SkillTransferItem skill, string id, string name)
    {
        var lines = skill.Content.Split('\n').ToList();
        var end = FrontmatterEnd(lines);
        if (end < 0) return skill;
        SetField(lines, end, "id", id);
        SetField(lines, end, "name", name);
        return skill with { Id = id, Name = name, Content = string.Join('\n', lines) };
    }

    private static string Portable(SkillTransferItem skill)
    {
        if (skill.Enabled) return skill.Content;
        var lines = skill.Content.Split('\n').ToList();
        var end = FrontmatterEnd(lines);
        if (end < 0) return skill.Content;
        lines.Insert(end, "enabled: false");
        return string.Join('\n', lines);
    }

    /// <summary>A folder or key per skill, numbered when two unsaved ones share an id.</summary>
    private static List<(string Folder, SkillTransferItem Skill)> Folders(IReadOnlyList<SkillTransferItem> skills)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<(string Folder, SkillTransferItem Skill)>(skills.Count);
        foreach (var skill in skills)
        {
            var id = skill.Id.Length > 0 ? skill.Id : "skill";
            var folder = id;
            var index = 1;
            while (!taken.Add(folder)) folder = $"{id}-{++index}";
            folders.Add((folder, skill));
        }

        return folders;
    }

    private static string? ReadJson(string text, Reader reader)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ReaderOptions);
        }
        catch (JsonException exception)
        {
            return exception.LineNumber is { } line
                ? $"This is not valid JSON (line {line + 1}, position {(exception.BytePositionInLine ?? 0) + 1})."
                : "This is not valid JSON.";
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && TryGet(root, "skills", out var skills)) root = skills;
            else if (root.ValueKind == JsonValueKind.Object && TryGet(root, "content", out _))
            {
                reader.ReadElement(root, "Skill");
                return null;
            }

            switch (root.ValueKind)
            {
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in root.EnumerateArray()) reader.ReadElement(item, $"Skill {++index}");
                    return null;
                case JsonValueKind.Object:
                    foreach (var property in root.EnumerateObject()) reader.ReadElement(property.Value, property.Name);
                    return null;
                default:
                    return "Nothing to import: expected JSON with a skills list.";
            }
        }
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    // Archivers on macOS add these beside every file.
    private static bool IsJunk(string path) =>
        path.StartsWith("__MACOSX/", StringComparison.Ordinal)
        || Path.GetFileName(path) is ".DS_Store" || Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);

    private static int FrontmatterEnd(List<string> lines) =>
        lines.Count > 2 && lines[0].TrimEnd() == Fence ? lines.FindIndex(1, line => line.TrimEnd() == Fence) : -1;

    private static bool IsField(string line, string key) =>
        line.StartsWith(key + ":", StringComparison.Ordinal);

    /// <summary>The first value of <paramref name="key"/>, read the way the Host reads it.</summary>
    private static string? Field(List<string> lines, string key)
    {
        var end = FrontmatterEnd(lines);
        if (end < 0) return null;
        var line = lines.Skip(1).Take(end - 1).FirstOrDefault(item => item.StartsWith(key + ": ", StringComparison.Ordinal));
        return line?[(key.Length + 2)..].Trim() is { Length: > 0 } value ? value : null;
    }

    private static void SetField(List<string> lines, int end, string key, string value)
    {
        var index = lines.FindIndex(1, end - 1, line => IsField(line, key));
        if (index >= 0) lines[index] = $"{key}: {value}";
        else lines.Insert(1, $"{key}: {value}");
    }

    /// <summary>The Host's id rule: lowercase letters, digits and hyphens, starting with a letter.</summary>
    private static bool IsId(string text) =>
        text.Length is >= 1 and <= MaxIdLength && text[0] is >= 'a' and <= 'z' && text[^1] != '-'
        && text.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>An id made from a name: "PDF tools" becomes <c>pdf-tools</c>.</summary>
    private static string? Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0) return null;
        if (slug[0] is not (>= 'a' and <= 'z')) slug = $"skill-{slug}";
        return slug.Length <= MaxIdLength ? slug : slug[..MaxIdLength].TrimEnd('-');
    }

    /// <summary>
    /// A YAML scalar as one line: block scalars (<c>description: &gt;</c> and indented lines) are
    /// joined, and quotes the Host would keep as part of the text are taken off.
    /// </summary>
    private static string Scalar(string value, List<string> lines, ref int index, int end)
    {
        if (value is "" or ">" or "|" or ">-" or "|-" or ">+" or "|+")
        {
            var parts = new List<string>();
            while (index + 1 < end && lines[index + 1] is { Length: > 0 } next && next[0] is ' ' or '\t')
            {
                parts.Add(next.Trim());
                index++;
            }

            value = string.Join(' ', parts.Where(part => part.Length > 0));
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            value = value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        return value.Trim();
    }

    /// <summary>Collects skills while reading one paste, file or archive.</summary>
    private sealed class Reader
    {
        private readonly List<ImportedSettingsItem<SkillTransferItem>> _skills = [];
        private readonly List<SkippedImportItem> _skipped = [];

        public SkillTransferParseResult Result(string emptyError) =>
            _skills.Count == 0 && _skipped.Count == 0
                ? SkillTransferParseResult.Failed(emptyError)
                : new SkillTransferParseResult(_skills, _skipped, null);

        public void Skip(string name, string reason) => _skipped.Add(new SkippedImportItem(name, reason));

        public void ReadElement(JsonElement element, string hint)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    Read(element.GetString()!, hint);
                    break;
                case JsonValueKind.Object when TryGet(element, "content", out var content) && content.ValueKind == JsonValueKind.String:
                    bool? enabled = TryGet(element, "enabled", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? flag.GetBoolean() : null;
                    Read(content.GetString()!, hint, [], enabled);
                    break;
                default:
                    Skip(hint, "Not a skill: expected the text of a SKILL.md.");
                    break;
            }
        }

        public void Read(string text, string hint, IReadOnlyList<string>? notes = null, bool? enabledOverride = null)
        {
            var normalized = text.TrimStart('﻿').ReplaceLineEndings("\n").Trim();
            if (normalized.Length > MaxLength)
            {
                Skip(hint, $"Longer than {MaxLength.ToString("N0", CultureInfo.InvariantCulture)} characters, the most a SKILL.md may hold.");
                return;
            }

            var lines = normalized.Split('\n').ToList();
            var end = FrontmatterEnd(lines);
            if (end < 0)
            {
                Skip(hint, "Not a SKILL.md: it needs frontmatter between --- lines.");
                return;
            }

            var front = new List<string>(end);
            var enabled = true;
            var foreignTools = false;
            var index = 0;
            while (++index < end)
            {
                var line = lines[index];
                if (IsField(line, "revision")) continue;
                if (IsField(line, "enabled"))
                {
                    enabled = !string.Equals(line["enabled:".Length..].Trim(), "false", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (IsField(line, "allowed-tools")) foreignTools = true;
                var key = IsField(line, "name") ? "name" : IsField(line, "description") ? "description" : null;
                if (key is null)
                {
                    front.Add(line);
                    continue;
                }

                front.Add($"{key}: {Scalar(line[(key.Length + 1)..].Trim(), lines, ref index, end)}");
            }

            string? Get(string key) => front.FirstOrDefault(line => line.StartsWith(key + ": ", StringComparison.Ordinal))
                ?[(key.Length + 2)..].Trim() is { Length: > 0 } value ? value : null;

            var name = Get("name");
            var id = Get("id");
            var label = name ?? id ?? hint;
            var description = Get("description");
            if (description is null)
            {
                Skip(label, "It has no description, which is what picks a skill for a task.");
                return;
            }

            if (description.Length > MaxDescriptionLength || name?.Length > MaxNameLength)
            {
                Skip(label, $"A skill name is up to {MaxNameLength} characters and a description up to {MaxDescriptionLength.ToString("N0", CultureInfo.InvariantCulture)}.");
                return;
            }

            // Agent Skills written for other clients name the skill and stop there; what this app
            // needs besides is filled in, and the note asks for a look before it is saved.
            var added = new List<string>(4);
            if (id is null)
            {
                id = Slug(name ?? string.Empty);
                if (id is null)
                {
                    Skip(label, "It has neither an id nor a name to make one from.");
                    return;
                }

                front.RemoveAll(line => IsField(line, "id"));
                front.Insert(0, $"id: {id}");
                added.Add("id");
            }
            else if (!IsId(id))
            {
                Skip(label, "Its id must be lowercase letters, digits and hyphens, starting with a letter.");
                return;
            }

            if (name is null)
            {
                name = id;
                front.RemoveAll(line => IsField(line, "name"));
                front.Insert(front.FindIndex(line => IsField(line, "id")) + 1, $"name: {name}");
                added.Add("name");
            }

            var kind = Get("kind");
            if (kind == "executor")
            {
                Skip(label, "Executor skills run bundled code and come only with the app.");
                return;
            }

            if (Get("parameters") is null)
            {
                front.RemoveAll(line => IsField(line, "parameters"));
                if (kind is null)
                {
                    front.Add("kind: playbook");
                    added.Add("kind");
                }

                front.Add($"parameters: {DefaultParameters}");
                added.Add("parameters");
            }

            var allNotes = new List<string>();
            if (added.Count > 0)
                allNotes.Add($"Written for another client: {string.Join(", ", added)} added. Check them, and the tools it names, after import.");
            if (foreignTools)
                allNotes.Add("allowed-tools is another client's list and is not used; a playbook names this app's tools in tools.");
            if (notes is not null) allNotes.AddRange(notes);

            var content = string.Join('\n', new[] { Fence }.Concat(front).Append(Fence).Concat(lines.Skip(end + 1))).Trim();
            _skills.Add(new ImportedSettingsItem<SkillTransferItem>(
                new SkillTransferItem(id, name, description, content, enabledOverride ?? enabled), false, allNotes));
        }
    }
}
