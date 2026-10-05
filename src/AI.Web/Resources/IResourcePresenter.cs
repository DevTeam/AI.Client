namespace AI.Web.Resources;

using AI.Contracts.Resources;

/// <summary>How a workspace reference in a sent message reads: in its "@" link and in its chip.</summary>
public interface IResourcePresenter
{
    /// <summary>
    /// The short name shown in place of the "@" token: a file or directory name, a chat title, a
    /// repository name. The kind is already told by the icon in front of it.
    /// </summary>
    string Label(ChatResource reference);

    /// <summary>
    /// The tooltip: the "@" token as written, then where it points and what a click does. For
    /// "@diff" it also lists the files the message captured.
    /// </summary>
    string Hint(ChatResource reference);

    /// <summary>
    /// Where its link points: a file URI for a file or directory (with the lines as a fragment),
    /// so it is a local path wherever it is drawn; "#mention-{id}" for everything else.
    /// </summary>
    string Target(ChatResource reference);

    /// <summary>For "@diff" the captured totals, such as "+120 −34"; null for every other kind.</summary>
    string? Summary(ChatResource reference);

    /// <summary>The changes a "@diff" reference captured, or null.</summary>
    DiffSnapshot? Changes(ChatResource reference);
}
