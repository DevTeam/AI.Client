namespace AI.Web.Composer;

using AI.Contracts.Runs;
using AI.Contracts.Settings;

/// <summary>
/// Turns the server's measurement of the last request into the layers the send button draws,
/// adding the message being typed with the same conservative estimate the Host uses.
/// </summary>
public interface IComposerContextPresentation
{
    ComposerContext Build(ContextUsage? usage, ConnectionSettings? connection, string draft);

    /// <summary>A compact token count: 950, 1.2k, 53k, 1.1M.</summary>
    string FormatTokens(long tokens);

    /// <summary>The one thing worth saying about the window now, or null when there is nothing.</summary>
    string? Note(ComposerContext context);
}
