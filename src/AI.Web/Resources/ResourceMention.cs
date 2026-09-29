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
public enum ResourceMentionGroup { Directories, Files, Changes, Chats, Reviews, Projects, Browse }

/// <summary>A directory the project may read, as its sidebar lists it: known before the Host is asked anything.</summary>
public sealed record ProjectDirectory(string Name, string Path);

/// <summary>What choosing a row does: attach it, or open the file or directory picker of the "+" menu.</summary>
public enum ResourceMentionAction { Attach, BrowseFile, BrowseDirectory }

/// <summary>
/// One row of the "@" list. <see cref="Value"/> is what the reference stores as its path: an
/// absolute path, or a chat, project or review id.
/// </summary>
/// <param name="Highlights">Indexes into <see cref="Name"/>, ascending.</param>
/// <param name="Token">The "@" link the row becomes in the message text, such as "@src/app.cs:12-40".</param>
/// <param name="OpenAs">For a directory: the word Tab turns the "@" word into to list what is inside, without the "/".</param>
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
    string? OpenAs = null)
{
    public string Key => $"{Group}:{Action}:{Value}";
}

/// <summary>Everything the "@" list can offer, as loaded by the page.</summary>
public sealed record ResourceMentionSources(
    IReadOnlyList<ResourceSuggestion> Files,
    IReadOnlyList<WorkspaceDiffSource> Diffs,
    IReadOnlyList<ChatSummary> Chats,
    IReadOnlyList<ChatReview> Reviews,
    IReadOnlyList<ProjectSummary> Projects,
    IReadOnlyList<ChatResourceRef> Attached,
    Guid? CurrentChatId,
    Guid? CurrentProjectId,
    IReadOnlyList<ProjectDirectory>? Directories = null);
