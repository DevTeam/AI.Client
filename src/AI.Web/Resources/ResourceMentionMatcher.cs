namespace AI.Web.Resources;

using System.Globalization;
using System.Text.RegularExpressions;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
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

    /// <summary>The most rows one opened level lists; the rest are counted and narrowed by typing.</summary>
    private const int LevelLimit = 200;

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

    public string? Folder(ResourceMention mention)
    {
        if (mention.Lines is not null || mention.Scope is not (null or ChatResourceKind.File or ChatResourceKind.Directory))
            return null;
        var filter = mention.Filter.Replace('\\', '/');
        var slash = filter.LastIndexOf('/');
        return slash < 0 ? null : filter[..(slash + 1)];
    }

    public IReadOnlyList<ResourceMentionItem> Match(ResourceMentionSources sources, ResourceMention mention)
    {
        if (sources.Trail is { Depth: > 0 } trail) return Level(sources, trail, mention.Filter);
        if (Folder(mention) is { } folder) return Listing(sources, mention, folder);
        var filter = mention.Filter;
        var scoped = mention.Scope is not null;
        var groups = new List<(ResourceMentionGroup Group, List<(ResourceMentionItem Item, int Rank)> Rows)>
        {
            (ResourceMentionGroup.Directories, Directories(sources, mention)),
            (ResourceMentionGroup.Files, Files(sources, mention)),
            (ResourceMentionGroup.Changes, In(mention, ChatResourceKind.Diff) ? Changes(sources, filter, scoped) : []),
            (ResourceMentionGroup.Chats, In(mention, ChatResourceKind.Chat) ? Chats(sources, filter) : []),
            (ResourceMentionGroup.OtherChats, In(mention, ChatResourceKind.Chat) ? OtherChats(sources, filter) : []),
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
            var expanded = scoped || sources.Expanded?.Contains(group) == true;
            var limit = expanded ? ScopedLimit : Limit(group, filter);
            var shown = rows.OrderBy(row => row.Rank).Take(limit).Select(row => row.Item).ToArray();
            result.AddRange(shown);
            // The Host may have found more than it sent: those are only counted.
            var found = group == ResourceMentionGroup.Files && mention.Scope is null or ChatResourceKind.File or ChatResourceKind.Directory
                ? Math.Max(rows.Count, sources.FilesTotal) : rows.Count;
            if (found > shown.Length) result.Add(More(group, found - shown.Length, !expanded && rows.Count > shown.Length));
        }
        AddBrowse(result, mention);
        return result;
    }

    /// <summary>
    /// "@App/src/": what is inside that one directory, all of it, folders first and then files by
    /// name, as the Host listed them. What follows the last "/" narrows the listing here, the way a
    /// name narrows the whole search, so typing within a directory asks the Host nothing new.
    /// </summary>
    private static List<ResourceMentionItem> Listing(ResourceMentionSources sources, ResourceMention mention,
        string folder)
    {
        var name = mention.Filter[folder.Length..];
        var parent = folder.TrimEnd('/');
        var rows = new List<(ResourceMentionItem Item, int Rank)>();
        foreach (var file in sources.Files)
        {
            if (mention.Scope is { } scope && file.Kind != scope) continue;
            var relative = file.RelativePath.TrimEnd('/');
            var slash = relative.LastIndexOf('/');
            var fileName = relative[(slash + 1)..];
            if (NameRank(fileName, name) is not { } ranked) continue;
            var directory = slash < 0 ? string.Empty : relative[..slash];
            var attached = sources.Attached.Any(item => item.Kind == file.Kind && SamePath(item.Path, file.Path)
                && item.Lines is null);
            var token = "@" + Quote(file.Kind == ChatResourceKind.Directory ? relative + "/" : relative);
            // The path above the list already names the directory; only a row found elsewhere says where.
            var detail = directory.Equals(parent, StringComparison.OrdinalIgnoreCase) ? null : directory;
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Files, file.Kind, fileName, detail, file.Path,
                ranked.Highlights, attached, Token: token,
                OpenAs: file.Kind == ChatResourceKind.Directory ? relative : null), ranked.Rank));
        }
        var result = rows.OrderBy(row => row.Rank).Select(row => row.Item).ToList();
        // A listing the Host cut short can be narrowed by a name, not shown further.
        if (name.Length == 0 && sources.FilesTotal > result.Count)
            result.Add(More(ResourceMentionGroup.Files, sources.FilesTotal - result.Count, expandable: false));
        AddBrowse(result, mention);
        return result;
    }

    /// <summary>
    /// An opened level: the chats of a project, newest first, or the branches of a chat, the main
    /// one first and each followed by those taken from it. The filter narrows it as a name does.
    /// </summary>
    private static List<ResourceMentionItem> Level(ResourceMentionSources sources, ResourceMentionTrail trail, string filter)
    {
        var now = sources.Now ?? DateTimeOffset.UtcNow;
        var project = trail.Project is { } opened && opened.Id != sources.CurrentProjectId ? opened : null;
        var rows = new List<(ResourceMentionItem Item, int Rank)>();
        var group = ResourceMentionGroup.Chats;
        if (trail.Chat is { } chat)
        {
            group = ResourceMentionGroup.Branches;
            if (sources.TrailChat is { } details && details.Id == chat.Id)
            {
                var id = details.Id.ToString();
                foreach (var (branch, count, headAt) in BranchesOf(details))
                {
                    var title = branch.Id == details.Id ? "Main branch" : branch.Title is { Length: > 0 } named ? named : "Branch";
                    if (NameRank(title, filter) is not { } ranked) continue;
                    var label = string.Join(" › ", new[] { project?.Name, details.Title, title }.OfType<string>());
                    var detail = (count == 1 ? "1 message" : $"{count} messages") + (headAt is { } at ? $" · {Ago(at, now)}" : string.Empty);
                    var attached = sources.Attached.Any(item => item.Kind == ChatResourceKind.Chat && item.Path == id
                        && item.BranchId == branch.Id);
                    rows.Add((new ResourceMentionItem(group, ChatResourceKind.Chat, title, detail, id, ranked.Highlights,
                        attached, Token: "@chat:" + Quote(label), ProjectId: project?.Id, BranchId: branch.Id,
                        FullName: label), ranked.Rank));
                }
            }
        }
        else
        {
            foreach (var item in Offered(sources, sources.TrailChats ?? []))
            {
                if (NameRank(item.Title, filter) is not { } ranked) continue;
                // The path above the list already names the project.
                rows.Add((ChatRow(sources, item, project, group, ranked.Highlights, now, nameProject: false), ranked.Rank));
            }
        }
        var result = rows.OrderBy(row => row.Rank).Take(LevelLimit).Select(row => row.Item).ToList();
        if (rows.Count > result.Count) result.Add(More(group, rows.Count - result.Count, expandable: false));
        return result;
    }

    /// <summary>
    /// The branches of a chat in the order the sidebar shows them, with how many messages lead to
    /// each one's head and when that head was written.
    /// </summary>
    private static IEnumerable<(ChatBranchView Branch, int Messages, DateTimeOffset? HeadAt)> BranchesOf(ChatDetails chat)
    {
        var branches = (chat.Branches ?? []).Where(branch => branch.HeadMessageId is not null).ToList();
        var children = branches.Where(branch => branch.Id != chat.Id).ToLookup(branch => branch.ParentBranchId ?? chat.Id);
        var ordered = new List<ChatBranchView>();
        var visited = new HashSet<Guid>();
        void Add(ChatBranchView branch)
        {
            if (!visited.Add(branch.Id)) return;
            ordered.Add(branch);
            foreach (var child in children[branch.Id]) Add(child);
        }
        if (branches.FirstOrDefault(branch => branch.Id == chat.Id) is { } main) Add(main);
        foreach (var child in children[chat.Id]) Add(child);
        // A branch whose parent is gone is still offered.
        foreach (var branch in branches) Add(branch);
        var messages = new Dictionary<Guid, ChatMessageView>();
        foreach (var message in chat.Messages) messages.TryAdd(message.Id, message);
        foreach (var branch in ordered)
        {
            var count = 0;
            var head = branch.HeadMessageId is { } headId && messages.TryGetValue(headId, out var found) ? found : null;
            for (var current = head; current is not null && count <= messages.Count;
                 current = current.ParentId is { } parent && messages.TryGetValue(parent, out var next) ? next : null)
                count++;
            yield return (branch, count, head?.CreatedAt);
        }
    }

    /// <summary>
    /// The chats a list offers, newest first: not archived ones or ones never written in, and not
    /// the current chat unless it has branches to open.
    /// </summary>
    private static IEnumerable<ChatSummary> Offered(ResourceMentionSources sources, IEnumerable<ChatSummary> chats) =>
        chats.Where(chat => !chat.IsEmpty && chat.ArchivedAt is null && (chat.Id != sources.CurrentChatId || chat.BranchCount > 0))
            .OrderByDescending(chat => chat.LastActivityAt);

    /// <summary>
    /// A chat as a row: when it was last written in and how many branches Tab opens. A chat of
    /// another project names that project first, in the row and in its link.
    /// </summary>
    private static ResourceMentionItem ChatRow(ResourceMentionSources sources, ChatSummary chat, ProjectSummary? project,
        ResourceMentionGroup group, IReadOnlyList<int> highlights, DateTimeOffset now, bool nameProject = true)
    {
        var details = new List<string>(3);
        if (project is not null && nameProject) details.Add(project.Name);
        details.Add(chat.Id == sources.CurrentChatId ? "This chat" : Ago(chat.LastActivityAt, now));
        // The count leaves out the main branch, which is the chat itself.
        if (chat.BranchCount > 0) details.Add($"{chat.BranchCount + 1} branches");
        var label = project is null ? chat.Title : $"{project.Name} › {chat.Title}";
        var id = chat.Id.ToString();
        var attached = sources.Attached.Any(item => item.Kind == ChatResourceKind.Chat && item.Path == id
            && item.BranchId is null && item.ProjectId == project?.Id);
        return new ResourceMentionItem(group, ChatResourceKind.Chat, chat.Title, string.Join(" · ", details), id,
            highlights, attached, Token: "@chat:" + Quote(label), ProjectId: project?.Id, HasLevel: chat.BranchCount > 0,
            FullName: label);
    }

    private static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var age = now - at;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        if (age < TimeSpan.FromDays(30)) return $"{(int)age.TotalDays} d ago";
        return at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static ResourceMentionItem More(ResourceMentionGroup group, int count, bool expandable) =>
        new(group, ChatResourceKind.File, $"{count} more", expandable ? "Show more" : "Type to narrow the list",
            $"more:{group}", [], Action: ResourceMentionAction.ShowMore, Expandable: expandable);

    private static void AddBrowse(List<ResourceMentionItem> result, ResourceMention mention)
    {
        if (mention.Lines is null && mention.Scope is null or ChatResourceKind.File or ChatResourceKind.Directory)
        {
            if (mention.Scope != ChatResourceKind.Directory)
                result.Add(new ResourceMentionItem(ResourceMentionGroup.Browse, ChatResourceKind.File, "Browse file…",
                    null, "file", [], Action: ResourceMentionAction.BrowseFile));
            if (mention.Scope != ChatResourceKind.File)
                result.Add(new ResourceMentionItem(ResourceMentionGroup.Browse, ChatResourceKind.Directory, "Browse directory…",
                    null, "directory", [], Action: ResourceMentionAction.BrowseDirectory));
        }
    }

    private static bool In(ResourceMention mention, ChatResourceKind kind) =>
        mention.Lines is null && (mention.Scope is null || mention.Scope == kind);

    private static int Limit(ResourceMentionGroup group, string filter) => group switch
    {
        ResourceMentionGroup.Directories => 10,
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
                slash < 0 ? null : relative[..slash], file.Path, ranked.Item2, attached, Lines: mention.Lines, Token: token,
                OpenAs: file.Kind == ChatResourceKind.Directory ? relative : null), ranked.Item1));
        }
        return rows;
    }

    /// <summary>
    /// The project's own directories, first and at once: the page has them already, so the list
    /// has something to offer before the Host answers. Tab opens one by its name ("@App/").
    /// </summary>
    private static List<(ResourceMentionItem, int)> Directories(ResourceMentionSources sources, ResourceMention mention)
    {
        if (mention.Scope is not (null or ChatResourceKind.Directory) || mention.Lines is not null
            || mention.Filter.Contains('/') || mention.Filter.Contains('\\')) return [];
        var rows = new List<(ResourceMentionItem, int)>();
        foreach (var directory in sources.Directories ?? [])
        {
            if (NameRank(directory.Name, mention.Filter) is not { } ranked) continue;
            var attached = sources.Attached.Any(item => item.Kind == ChatResourceKind.Directory && SamePath(item.Path, directory.Path));
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Directories, ChatResourceKind.Directory, directory.Name,
                directory.Path, directory.Path, ranked.Highlights, attached, Token: "@" + Quote(directory.Name + "/"),
                OpenAs: directory.Name), ranked.Rank));
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
            var count = diff.ChangedFiles == 1 ? "1 changed file" : $"{diff.ChangedFiles} changed files";
            var detail = diff.Location is { } location && !location.Equals(diff.Name, StringComparison.Ordinal)
                ? $"{location} · {count}" : count;
            rows.Add((new ResourceMentionItem(ResourceMentionGroup.Changes, ChatResourceKind.Diff, diff.Name, detail,
                    diff.Path, byName?.Highlights ?? [], sources.Attached.Any(item => item.Kind == ChatResourceKind.Diff
                        && SamePath(item.Path, diff.Path)),
                    // Always named: a bare "@diff" does not say which repository it was.
                    Token: "@diff:" + Quote(diff.Name)),
                Math.Min(byName?.Rank ?? int.MaxValue, byWord ?? int.MaxValue)));
        }
        return rows;
    }

    private static List<(ResourceMentionItem, int)> Chats(ResourceMentionSources sources, string filter)
    {
        var rows = new List<(ResourceMentionItem, int)>();
        var now = sources.Now ?? DateTimeOffset.UtcNow;
        foreach (var chat in Offered(sources, sources.Chats))
        {
            if (NameRank(chat.Title, filter) is not { } ranked) continue;
            rows.Add((ChatRow(sources, chat, null, ResourceMentionGroup.Chats, ranked.Highlights, now), ranked.Rank));
        }
        return rows;
    }

    /// <summary>Recent chats of the other projects, each named with its project.</summary>
    private static List<(ResourceMentionItem, int)> OtherChats(ResourceMentionSources sources, string filter)
    {
        var rows = new List<(ResourceMentionItem, int)>();
        var now = sources.Now ?? DateTimeOffset.UtcNow;
        foreach (var chat in Offered(sources, (sources.OtherChats ?? []).Where(chat => chat.ProjectId != sources.CurrentProjectId)))
        {
            if (sources.Projects.FirstOrDefault(project => project.Id == chat.ProjectId) is not { } project
                || NameRank(chat.Title, filter) is not { } ranked) continue;
            rows.Add((ChatRow(sources, chat, project, ResourceMentionGroup.OtherChats, ranked.Highlights, now), ranked.Rank));
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
                Token: "@project:" + Quote(project.Name), HasLevel: true), ranked.Rank));
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
