namespace AI.Web.Composer;

public enum ContextLayerKind { Instructions, Tools, History, Draft, ReservedOutput, Overhead }

public enum ContextFillLevel { Normal, Warning, Critical }

/// <summary>One slice of the context window: what it holds and how many tokens it takes.</summary>
/// <param name="Reserved">Space held back from the input rather than tokens already sent.</param>
public sealed record ContextLayer(ContextLayerKind Kind, string Label, long Tokens, bool Reserved)
{
    /// <summary>The suffix of the layer's colour class, context-layer-*, in app.css.</summary>
    public string CssName => Kind switch
    {
        ContextLayerKind.Instructions => "instructions",
        ContextLayerKind.Tools => "tools",
        ContextLayerKind.History => "history",
        ContextLayerKind.Draft => "draft",
        ContextLayerKind.ReservedOutput => "reserve",
        _ => "overhead"
    };
}

/// <summary>
/// The context window as the composer shows it: the layers of the last measured request of the
/// branch, plus the message being typed. Layers are in drawing order, from 12 o'clock clockwise.
/// </summary>
/// <param name="IsMeasured">
/// False until the branch has sent a request this Host session: only the draft and the reserved
/// layers are known then, so the ring must not read as an almost empty window.
/// </param>
public sealed record ComposerContext(
    long CapacityTokens,
    bool IsDefaultCapacity,
    IReadOnlyList<ContextLayer> Layers,
    bool IsMeasured,
    bool WasCompacted,
    int OmittedMessages)
{
    public long UsedTokens => Layers.Sum(layer => layer.Tokens);

    public double Fraction => CapacityTokens <= 0 ? 1 : Math.Min(1, (double)UsedTokens / CapacityTokens);

    public int Percent => (int)Math.Round(Fraction * 100);

    public ContextFillLevel Level => Fraction switch
    {
        >= 0.9 => ContextFillLevel.Critical,
        >= 0.75 => ContextFillLevel.Warning,
        _ => ContextFillLevel.Normal
    };
}
