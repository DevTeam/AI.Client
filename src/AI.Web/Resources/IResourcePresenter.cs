namespace AI.Web.Resources;

using AI.Contracts.Resources;

/// <summary>How a workspace reference in a sent message reads: in its "@" link and in its chip.</summary>
public interface IResourcePresenter
{
    /// <summary>
    /// The short name shown in place of the "@" token: a file or directory name, a chat title, a
    /// repository name. The kind is already told by the icon in front of it.
    /// </summary>
    string Label(ChatResourceRef reference);

    /// <summary>
    /// The tooltip: the "@" token as written, then where it points and what a click does. For
    /// "@diff" it also lists the files the message captured.
    /// </summary>
    string Hint(ChatResourceRef reference);

    /// <summary>For "@diff" the captured totals, such as "+120 −34"; null for every other kind.</summary>
    string? Summary(ChatResourceRef reference);

    /// <summary>The changes a "@diff" reference captured, or null.</summary>
    DiffSnapshot? Changes(ChatResourceRef reference);
}
