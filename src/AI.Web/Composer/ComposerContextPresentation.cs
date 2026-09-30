namespace AI.Web.Composer;

using System.Globalization;
using System.Text;
using AI.Contracts.Runs;
using AI.Contracts.Settings;

public sealed class ComposerContextPresentation(IConnectionContextLimitsResolver limits) : IComposerContextPresentation
{
    // Mirrors ChatContextPlanner: protocol framing plus the tokenizer safety margin. Used only
    // until the branch has been measured; afterwards the Host's own figure is shown.
    private const long DefaultOverheadTokens = 256 + 1_024;

    // Mirrors ContextTokenEstimator: a message envelope and two UTF-8 bytes per token.
    private const long MessageEnvelopeTokens = 12;

    public ComposerContext Build(ContextUsage? usage, ConnectionSettings? connection, string draft)
    {
        // The window and the answer reserve follow the connection picked now, which may not be
        // the one the last request went to; what the request contained does not change with it.
        var resolved = limits.Resolve(connection);
        var layers = new List<ContextLayer>
        {
            new(ContextLayerKind.Instructions, "Instructions", usage?.InstructionTokens ?? 0, false),
            new(ContextLayerKind.Tools, "Tools", usage?.ToolTokens ?? 0, false),
            new(ContextLayerKind.History, "Conversation", usage?.HistoryTokens ?? 0, false),
            new(ContextLayerKind.Draft, "Your message", EstimateDraft(draft), false),
            new(ContextLayerKind.ReservedOutput, "Answer reserve", resolved.ReservedOutputTokens, true),
            new(ContextLayerKind.Overhead, "Overhead", usage?.OverheadTokens ?? DefaultOverheadTokens, true)
        };
        return new ComposerContext(resolved.ContextWindowTokens,
            resolved.ContextWindowSource == ContextLimitSource.Default, layers,
            usage is not null, usage?.WasCompacted ?? false, usage?.OmittedMessages ?? 0);
    }

    public string FormatTokens(long tokens) => tokens switch
    {
        < 1_000 => tokens.ToString(CultureInfo.InvariantCulture),
        < 10_000 => (tokens / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        < 1_000_000 => (tokens / 1_000d).ToString("0", CultureInfo.InvariantCulture) + "k",
        _ => (tokens / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M"
    };

    public string? Note(ComposerContext context) => context switch
    {
        { Level: ContextFillLevel.Critical } => "Almost full: the next request compacts older turns",
        { Level: ContextFillLevel.Warning } => "Filling up: older turns will be compacted soon",
        { WasCompacted: true, OmittedMessages: > 0 } => $"Older turns compacted to fit ({context.OmittedMessages} messages)",
        { WasCompacted: true } => "Older turns compacted to fit",
        { IsMeasured: false } => "Measured with the next request",
        { IsDefaultCapacity: true } => "Default window size; set the real one in Connections",
        _ => null
    };

    private static long EstimateDraft(string draft) =>
        string.IsNullOrEmpty(draft) ? 0 : MessageEnvelopeTokens + EstimateText("user") + EstimateText(draft);

    private static long EstimateText(string value) => (Encoding.UTF8.GetByteCount(value) + 1L) / 2L;
}
