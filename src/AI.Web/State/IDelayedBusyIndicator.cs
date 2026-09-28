namespace AI.Web.State;

// CA1716: `End` matches the existing public method on the concrete `DelayedBusyIndicator`, and
// changing the interface would force every consumer to translate calls just to satisfy a VB.NET
// keyword check the project does not otherwise care about.
#pragma warning disable CA1716

/// <summary>
/// A busy flag that lags on purpose. The contract exposes only what the owner needs:
/// start a load, end it, and ask whether the placeholder should be on screen right now.
/// </summary>
/// <remarks>
/// The full justification lives on <see cref="DelayedBusyIndicator"/>: a flag that answered
/// every load instantly would flash a placeholder for two frames and read as a render glitch
/// rather than as feedback, while a load that only just crossed the threshold would blink out
/// the moment the eye arrived. Both ends are damped — the implementation hides those delays
/// behind this minimal surface so callers do not have to know about them.
/// </remarks>
public interface IDelayedBusyIndicator : IDisposable
{
    /// <summary>Whether the owner should render its placeholder right now.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// Marks the start of a load. Call it only when there is nothing on screen worth keeping:
    /// a background refresh of a list the user is already reading must not replace it with bones.
    /// </summary>
    void Begin();

    /// <summary>
    /// Marks the end of a load, and returns immediately: the placeholder may outlive the call
    /// by up to its minimum-visible time. Idempotent so every path out of a load — including
    /// the early returns and the failing ones — can call it without bookkeeping.
    /// </summary>
    void End();
}

#pragma warning restore CA1716
