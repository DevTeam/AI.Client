namespace AI.Web.Resources;

using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Resources;

/// <summary>
/// The "@word" the caret is in. <see cref="Start"/> is the index of "@" and <see cref="End"/> the
/// end of the word, so accepting a row replaces exactly what was typed. "@chat" or "@chat:deploy"
/// narrows to one <see cref="Scope"/> and filters it by what follows the colon; "@app.cs:12-40"
/// names lines of a file.
/// </summary>
public sealed record ResourceMention(int Start, int End, string Query, ChatResourceKind? Scope, string Filter,
    ChatLineRange? Lines);

/// <summary>The sections of the "@" list, in the order they are shown when nothing ranks them.</summary>
public enum ResourceMentionGroup { Directories, Files, Changes, Chats, OtherChats, Branches, Reviews, Projects, Browse }

/// <summary>A directory the project may read, as its sidebar lists it: known before the Host is asked anything.</summary>
public sealed record ProjectDirectory(string Name, string Path);

/// <summary>
/// What choosing a row does: attach it, open the file or directory picker of the "+" menu, or show
/// the rows a group left out.
/// </summary>
public enum ResourceMentionAction { Attach, BrowseFile, BrowseDirectory, ShowMore }

/// <summary>
/// One row of the "@" list. <see cref="Value"/> is what the reference stores as its path: an
/// absolute path, or a chat, project or review id.
/// </summary>
/// <param name="Highlights">Indexes into <see cref="Name"/>, ascending.</param>
/// <param name="Token">The "@" link the row becomes in the message text, such as "@src/app.cs:12-40".</param>
/// <param name="OpenAs">For a directory: the word Tab turns the "@" word into to list what is inside, without the "/".</param>
/// <param name="Expandable">For a "more" row: whether choosing it shows the rest, rather than only counting them.</param>
/// <param name="ProjectId">For a chat: its project, when that is not the current one.</param>
/// <param name="BranchId">For a chat: the one branch the row is, or null for the whole chat.</param>
/// <param name="HasLevel">A chat with branches or a project: Tab and → list what is inside instead of attaching it.</param>
/// <param name="FullName">The name the reference keeps, with the project and chat a branch row leaves to the path above it.</param>
public sealed record ResourceMentionItem(
    ResourceMentionGroup Group,
    ChatResourceKind Kind,
    string Name,
    string? Detail,
    string Value,
    IReadOnlyList<int> Highlights,
    bool Attached = false,
    ResourceMentionAction Action = ResourceMentionAction.Attach,
    ChatLineRange? Lines = null,
    ChatReviewKind? ReviewKind = null,
    string Token = "",
    string? OpenAs = null,
    bool Expandable = false,
    Guid? ProjectId = null,
    Guid? BranchId = null,
    bool HasLevel = false,
    string? FullName = null)
{
    public string Key => $"{Group}:{Action}:{Value}:{BranchId}";

    /// <summary>Whether Tab and → open the row as a level of the list rather than attach it.</summary>
    public bool Opens => HasLevel || Kind == ChatResourceKind.Directory && Action == ResourceMentionAction.Attach;
}

/// <summary>
/// The levels opened from the "@" list: a project, then one of its chats to list its branches. The
/// text keeps only the scope word and the filter; the names stay here, since a title has spaces.
/// </summary>
public sealed record ResourceMentionTrail(ProjectSummary? Project, ChatSummary? Chat)
{
    public int Depth => (Project is null ? 0 : 1) + (Chat is null ? 0 : 1);
}

/// <summary>
/// Everything the "@" list can offer, as loaded by the page. <see cref="FilesTotal"/> is how many
/// files the Host found, sent or not; <see cref="Expanded"/> are the groups shown whole.
/// <see cref="OtherChats"/> are recent chats of every project; <see cref="TrailChats"/> and
/// <see cref="TrailChat"/> are what the opened <see cref="Trail"/> lists, null until loaded.
/// </summary>
public sealed record ResourceMentionSources(
    IReadOnlyList<ResourceSuggestion> Files,
    IReadOnlyList<WorkspaceDiffSource> Diffs,
    IReadOnlyList<ChatSummary> Chats,
    IReadOnlyList<ChatReview> Reviews,
    IReadOnlyList<ProjectSummary> Projects,
    IReadOnlyList<ChatResource> Attached,
    Guid? CurrentChatId,
    Guid? CurrentProjectId,
    IReadOnlyList<ProjectDirectory>? Directories = null,
    int FilesTotal = 0,
    IReadOnlyCollection<ResourceMentionGroup>? Expanded = null,
    IReadOnlyList<ChatSummary>? OtherChats = null,
    ResourceMentionTrail? Trail = null,
    IReadOnlyList<ChatSummary>? TrailChats = null,
    ChatDetails? TrailChat = null,
    DateTimeOffset? Now = null);
