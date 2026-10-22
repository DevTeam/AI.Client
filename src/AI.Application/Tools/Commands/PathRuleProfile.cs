namespace AI.Application.Tools.Commands;

using System.Text.Json;
using AI.Contracts.FileSystem;
using AI.Contracts.Settings;

/// <summary>
/// A built-in file tool: a pattern is a path below the project (<c>docs/**</c>), matched against
/// every path the call changes. Paths that hold secrets or the repository's own machinery, and
/// paths outside the project, are never allowed unasked; deletions only by an exact path.
/// </summary>
/// <param name="pathArguments">The arguments that name a path the call changes; a path it only reads is not one.</param>
public sealed class PathRuleProfile(IPath paths, string tool, IReadOnlyList<string> pathArguments, bool deletes) : IToolRuleProfile
{
    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".ssh", ".aws", ".azure", ".gnupg", ".kube", ".docker", ".npmrc", ".pypirc", ".netrc",
        ".git-credentials", ".pgpass"
    };

    private static readonly string[] SecretExtensions = [".pem", ".key", ".pfx", ".p12", ".jks", ".keystore"];

    public Guid ServerId => DefaultMcpServer.Id;

    public string Tool => tool;

    public bool TryParsePattern(string text, out IRulePattern? pattern, out string? problem)
    {
        pattern = PathPattern.TryParse(text, paths, out var parsed, out problem) ? parsed : null;
        return pattern is not null;
    }

    public Task<RuleSubject?> ReadAsync(string arguments, RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var targets = Paths(arguments).Select(path => Target(path, context.Grants)).ToArray();
        if (targets.Length == 0) return Task.FromResult<RuleSubject?>(null);
        var warnings = new List<string>();
        var blocks = false;
        foreach (var target in targets)
        {
            var parts = target.Full.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (target.Relative is null)
            {
                warnings.Add($"It changes a path outside the project: {target.Full}.");
                blocks = true;
            }
            if (parts.Any(part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("It changes the repository's own files under .git, which can make git run code.");
                blocks = true;
            }
            if (parts.Length > 0 && Secret(parts))
            {
                warnings.Add($"It touches a file that may hold secrets: {target.Full}.");
                blocks = true;
            }
        }
        if (deletes) warnings.Add("It deletes for good what it names.");
        var display = $"{tool} {string.Join(" → ", targets.Select(Show))}";
        var subject = new RuleSubject(display, warnings.Distinct(StringComparer.Ordinal).ToArray(), blocks, deletes,
            Options(targets), new PathCall(targets), null,
            new { paths = targets.Select(target => target.Full).ToArray(), insideProject = targets.All(target => target.Relative is not null), deletes });
        return Task.FromResult<RuleSubject?>(subject);
    }

    /// <summary>
    /// The folder holding what the call changes, with everything below it; the exact path; the
    /// whole project. A deletion is offered only as its exact path.
    /// </summary>
    private string[] Options(PathTarget[] targets)
    {
        if (targets.Any(target => target.Relative is null || target.Relative.Count == 0 || target.Relative.Any(Masks.IsMask)))
            return [];
        var exact = targets.Length == 1 ? string.Join('/', targets[0].Relative!) : null;
        if (deletes) return exact is null ? [] : [exact];
        var folders = targets.Select(target => target.Relative!.Take(target.Relative!.Count - 1).ToArray()).ToArray();
        var common = folders[0].TakeWhile((part, index) => folders.All(folder => folder.Length > index
            && string.Equals(folder[index], part, paths.Comparison))).ToArray();
        var options = new List<string>();
        if (common.Length > 0) options.Add(string.Join('/', common) + "/**");
        if (exact is not null) options.Add(exact);
        options.Add("**");
        return options.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Show(PathTarget target) => target.Relative is { Count: > 0 } relative ? string.Join('/', relative) : target.Full;

    private static bool Secret(string[] parts) =>
        parts.Any(SecretNames.Contains)
        || parts[^1].StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
        || parts[^1].StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase)
        || parts[^1].StartsWith("id_ed25519", StringComparison.OrdinalIgnoreCase)
        || parts[^1].StartsWith("id_ecdsa", StringComparison.OrdinalIgnoreCase)
        || SecretExtensions.Any(extension => parts[^1].EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private PathTarget Target(string path, IReadOnlyList<CommandGrant> grants)
    {
        var full = paths.GetFullPath(path);
        var grant = grants.Where(item => paths.IsInside(full, item.Root, item.Recursive))
            .OrderByDescending(item => item.Root.Length)
            .FirstOrDefault();
        if (grant is null) return new PathTarget(full, null);
        var relative = paths.GetRelativePath(grant.Root, full);
        return new PathTarget(full, relative is "" or "." ? [] : relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The fully qualified paths the call changes; the session has already made them so.</summary>
    private IEnumerable<string> Paths(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object) yield break;
        foreach (var name in pathArguments)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            var values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value];
            foreach (var item in values)
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } path && paths.IsFullyQualified(path))
                    yield return path;
        }
    }
}

/// <summary>Every tool command rules can decide; <see cref="CommandRule.Tools"/> lists the same names.</summary>
public sealed class ToolRuleProfiles : IToolRuleProfiles
{
    public ToolRuleProfiles(ICommandParser parser, IPath paths)
    {
        All =
        [
            new ProcessRunRuleProfile(parser, paths),
            new PathRuleProfile(paths, "write_file", ["path"], false),
            new PathRuleProfile(paths, "edit_file", ["path"], false),
            new PathRuleProfile(paths, "create_directory", ["path"], false),
            new PathRuleProfile(paths, "move_file", ["source", "destination"], false),
            new PathRuleProfile(paths, "delete_file", ["path"], true),
            new PathRuleProfile(paths, "delete_directory", ["path"], true),
            new PathRuleProfile(paths, "zip_create", ["path"], false),
            new PathRuleProfile(paths, "zip_extract", ["destination"], false)
        ];
    }

    public IReadOnlyList<IToolRuleProfile> All { get; }

    public IToolRuleProfile? Find(string tool) => All.FirstOrDefault(profile => profile.Tool == tool);

    public IToolRuleProfile? Find(Guid serverId, string tool) =>
        All.FirstOrDefault(profile => profile.ServerId == serverId && profile.Tool == tool);
}
