namespace AI.Web.Resources;

using AI.Contracts.Resources;

/// <summary>Turns the "@" links a message was written with into links the transcript can draw.</summary>
public interface IMentionLinkWriter
{
    /// <summary>
    /// The message markdown with each reference's <see cref="ChatResourceRef.Mention"/> written as a
    /// markdown link: a file URI for a file or directory, so it becomes an ordinary local-path link,
    /// and "#mention-{id}" for the rest. The link reads as <see cref="IResourcePresenter.Label"/>
    /// and its title is the hint; the class names carry the kind for the icon in front, and a
    /// "@diff" link carries its totals in <c>data-summary</c>.
    /// </summary>
    string Link(string markdown, IReadOnlyList<ChatResourceRef>? resources);
}
