namespace AI.Web.Resources;

using System.Globalization;
using System.Text.RegularExpressions;
using AI.Contracts.Resources;

public sealed partial class ResourceMentionMatcher : IResourceMentionMatcher
{
    private static readonly Dictionary<string, ChatResourceKind> Scopes =
        new Dictionary<string, ChatResourceKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = ChatResourceKind.File,
            ["dir"] = ChatResourceKind.Directory,
            ["folder"] = ChatResourceKind.Directory,
            ["chat"] = ChatResourceKind.Chat,
            ["project"] = ChatResourceKind.Project,
            ["diff"] = ChatResourceKind.Diff,
            ["changes"] = ChatResourceKind.Diff,
            ["review"] = ChatResourceKind.Review
        };

    /// <summary>What "@diff" and "@changes" find among the rows for uncommitted changes.</summary>
    private static readonly string[] DiffWords = ["diff", "changes"];

    private const int ScopedLimit = 50;

    public ResourceMention? GetMention(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        if (start >= caret || text[start] != '@') return null;
        var end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var query = text[(start + 1)..end];
        // A quoted link (@chat:"Deploy fix") is one already written, not a word being typed.
        if (query.Contains('"', StringComparison.Ordinal)) return null;
        ChatResourceKind? scope = null;
        var filter = query;
        var colon = query.IndexOf(':', StringComparison.Ordinal);
        var word = colon < 0 ? query : query[..colon];
        if (Scopes.TryGetValue(word, out var kind))
        {
            scope = kind;
            filter = colon < 0 ? string.Empty : query[(colon + 1)..];
        }
        ChatLineRange? lines = null;
        if (scope is null or ChatResourceKind.File && LineSuffix().Match(filter) is { Success: true } suffix)
        {
            var first = int.Parse(suffix.Groups["first"].Value, CultureInfo.InvariantCulture);
            var last = suffix.Groups["last"].Success ? int.Parse(suffix.Groups["last"].Value, CultureInfo.InvariantCulture) : first;
            if (first > 0 && last > 0)
            {
                lines = new ChatLineRange(Math.Min(first, last), Math.Max(first, last));
                filter = suffix.Groups["name"].Value;
                scope = ChatResourceKind.File;
            }
        }
        return new ResourceMention(start, end, query, scope, filter, lines);
    }

    public IReadOnlyList<ResourceMentionItem> Match(ResourceMentionSources sources, ResourceMention mention)
    {
        var filter = mention.Filter;
        var scoped = mention.Scope is not null;
        var groups = new List<(ResourceMentionGroup Group, List<(ResourceMentionItem Item, int Rank)> Rows)>
        {
            (ResourceMentionGroup.Files, Files(sources, mention)),
            (ResourceMentionGroup.Changes, In(mention, ChatResourceKind.Diff) ? Changes(sources, filter, scoped) : []),
            (ResourceMentionGroup.Chats, In(mention, ChatResourceKind.Chat) ? Chats(sources, filter) : []),
            (ResourceMentionGroup.Reviews, In(mention, ChatResourceKind.Review) ? Reviews(sources, filter) : []),
            (ResourceMentionGroup.Projects, In(mention, ChatResourceKind.Project) ? Projects(sources, filter) : [])
        };
        var ordered = filter.Length == 0 ? groups : groups
            .Where(group => group.Rows.Count > 0)
            .OrderBy(group => group.Rows.Min(row => row.Rank))
            .ThenBy(group => group.Group)
            .ToList();
        var result = new List<ResourceMentionItem>();
        foreach (var (group, rows) in ordered)
        {
            var limit = scoped ? ScopedLimit : Limit(group, filter);
            result.AddRange(rows.OrderBy(row => row.Rank).Take(limit).Select(row => row.Item));
        }
        if (mention.Lines is null && mention.Scope is null or ChatResourceKind.File or ChatResourceKind.Directory)
        {
            if (mention.Scope != ChatResourceKind.Directory)
                result.Add(new ResourceMentionItem(ResourceMentionGroup.Browse, ChatResourceKind.File, "Browse file…",
                    null, "file", [], Action: ResourceMentionAction.BrowseFile));
            if (mention.Scope != ChatResourceKind.File)
                result.Add(new ResourceMentionItem(ResourceMentionGroup.Browse, ChatResourceKind.Directory, "Browse directory…",
                    null, "directory", [], Action: ResourceMentionAction.BrowseDirectory));
        }
        return result;
    }

    private static bool In(ResourceMention mention, ChatResourceKind kind) =>
        mention.Lines is null && (mention.Scope is null || mention.Scope == kind);

    private static int Limit(ResourceMentionGroup group, string filter) => group switch
    {
        ResourceMentionGroup.Files => filter.Length == 0 ? 5 : 8,
        ResourceMentionGroup.Chats => 5,
        _ => 3
    };

    /// <summary>
    /// Files come ranked by the Host, which searched the disk; here they only get their highlights
    /// and a rank comparable with the other groups. The Host's order breaks ties.
    /// </summary>
    private static List<(ResourceMentionItem, int)> Files(ResourceMentionSources sources, ResourceMention mention)
    {
        if (mention.Scope is not (null or ChatResourceKind.File or ChatResourceKind.Directory)) return [];
        var rows = new List<(ResourceMentionItem, int)>();
        var pathQuery = mention.Filter.Contains('/') || mention.Filter.Contains('\\');
        foreach (var file in sources.Files)
        {
            if (mention.Scope is { } scope && file.Kind != scope) continue;
            if (mention.Lines is not null && file.Kind != ChatResourceKind.File) continue;
            var relative = file.RelativePath.TrimEnd('/');
            var slash = relative.LastIndexOf('/');
            var name = relative[(slash + 1)..];
            var ranked = pathQuery ? (1, []) : NameRank(name, mention.Filter) ?? (4, (IReadOnlyList<int>)[]);
            var shown = mention.Lines is { } lines ? $"{name}:{Range(lines)}" : name;
            var attached = sources.Attached.Any(item => item.Kind == file.Kind && SamePath(item.Path, file.Path)
                && Equals(item.Lines, mention.Lines));
            var token = "@" + Quote(file.Kind == ChatResourceKind.Directory ? relative + "/" : relative)
                        + (mention.Lines is { } range ? $":{Range(range)}" : string.Empty);
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Files, file.Kind, shown,
                slash < 0 ? null : relative[..slash], file.Path, ranked.Item2, attached, Lines: mention.Lines, Token: token), ranked.Item1));
        }
        return rows;
    }

    private static List<(ResourceMentionItem, int)> Changes(ResourceMentionSources sources, string filter, bool scoped)
    {
        var rows = new List<(ResourceMentionItem, int)>();
        foreach (var diff in sources.Diffs)
        {
            var byName = NameRank(diff.Name, filter);
            // "@di" is on its way to "@diff": every repository with changes matches the word.
            var byWord = DiffWords.Any(word => word.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) ? 1 : (int?)null;
            if (!scoped && byName is null && byWord is null) continue;
            if (scoped && filter.Length > 0 && byName is null) continue;
            var detail = diff.ChangedFiles == 1 ? "1 changed file" : $"{diff.ChangedFiles} changed files";
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Changes, ChatResourceKind.Diff, diff.Name, detail,
                    diff.Path, byName?.Highlights ?? [], sources.Attached.Any(item => item.Kind == ChatResourceKind.Diff
                        && SamePath(item.Path, diff.Path)),
                    Token: sources.Diffs.Count == 1 ? "@diff" : "@diff:" + Quote(diff.Name)),
                Math.Min(byName?.Rank ?? int.MaxValue, byWord ?? int.MaxValue)));
        }
        return rows;
    }

    private static List<(ResourceMentionItem, int)> Chats(ResourceMentionSources sources, string filter)
    {
        var rows = new List<(ResourceMentionItem, int)>();
        foreach (var chat in sources.Chats.Where(chat => chat.Id != sources.CurrentChatId && !chat.IsEmpty)
                     .OrderByDescending(chat => chat.LastActivityAt))
        {
            if (NameRank(chat.Title, filter) is not { } ranked) continue;
            var id = chat.Id.ToString();
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Chats, ChatResourceKind.Chat, chat.Title, "Chat", id,
                ranked.Highlights, sources.Attached.Any(item => item.Kind == ChatResourceKind.Chat && item.Path == id),
                Token: "@chat:" + Quote(chat.Title)), ranked.Rank));
        }
        return rows;
    }

    private static List<(ResourceMentionItem, int)> Reviews(ResourceMentionSources sources, string filter)
    {
        var rows = new List<(ResourceMentionItem, int)>();
        foreach (var review in sources.Reviews)
        {
            if (NameRank(review.Name, filter) is not { } ranked) continue;
            var count = review.Kind == ChatReviewKind.Diff ? review.Comments.Count : review.MessageComments?.Count ?? 0;
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Reviews, ChatResourceKind.Review, review.Name,
                count == 1 ? "1 comment" : $"{count} comments", review.Id.ToString(), ranked.Highlights,
                sources.Attached.Any(item => item.Id == review.Id), ReviewKind: review.Kind,
                Token: "@review:" + Quote(review.Name)), ranked.Rank));
        }
        return rows;
    }

    private static List<(ResourceMentionItem, int)> Projects(ResourceMentionSources sources, string filter)
    {
        var rows = new List<(ResourceMentionItem, int)>();
        foreach (var project in sources.Projects.Where(project => project.Id != sources.CurrentProjectId))
        {
            if (NameRank(project.Name, filter) is not { } ranked) continue;
            var id = project.Id.ToString();
            var detail = string.IsNullOrWhiteSpace(project.Description) ? "Project" : project.Description.ReplaceLineEndings(" ");
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Projects, ChatResourceKind.Project, project.Name, detail, id,
                ranked.Highlights, sources.Attached.Any(item => item.Kind == ChatResourceKind.Project && item.Path == id),
                Token: "@project:" + Quote(project.Name)), ranked.Rank));
        }
        return rows;
    }

    /// <summary>
    /// The order the slash list uses: prefix, start of a word, anywhere, then the letters in order
    /// ("hrz" finds "Home.razor"). Lower is better; null is no match.
    /// </summary>
    private static (int Rank, IReadOnlyList<int> Highlights)? NameRank(string name, string query)
    {
        if (query.Length == 0) return (0, []);
        const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;
        if (name.StartsWith(query, ignoreCase)) return (0, Enumerable.Range(0, query.Length).ToArray());
        for (var index = name.IndexOf(query, ignoreCase); index > 0; index = name.IndexOf(query, index + 1, ignoreCase))
        {
            if (!char.IsLetterOrDigit(name[index - 1]) || char.IsUpper(name[index]) && char.IsLower(name[index - 1]))
                return (2, Enumerable.Range(index, query.Length).ToArray());
        }
        if (name.IndexOf(query, ignoreCase) is var inside and >= 0) return (3, Enumerable.Range(inside, query.Length).ToArray());
        var highlights = new List<int>(query.Length);
        var position = 0;
        foreach (var character in query)
        {
            var found = -1;
            for (var index = position; index < name.Length && found < 0; index++)
                if (char.ToUpperInvariant(name[index]) == char.ToUpperInvariant(character)) found = index;
            if (found < 0) return null;
            highlights.Add(found);
            position = found + 1;
        }
        return (4, highlights);
    }

    /// <summary>A name with spaces is quoted so the link stays one word; a quote inside becomes an apostrophe.</summary>
    public static string Quote(string name) =>
        name.Any(char.IsWhiteSpace) || name.Contains('"', StringComparison.Ordinal)
            ? $"\"{name.Replace('"', '\'')}\"" : name;

    private static string Range(ChatLineRange lines) =>
        lines.Start == lines.End ? $"{lines.Start}" : $"{lines.Start}-{lines.End}";

    private static bool SamePath(string left, string right) =>
        string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<name>.+?):(?<first>\d{1,7})(?:-(?<last>\d{1,7}))?$")]
    private static partial Regex LineSuffix();
}
