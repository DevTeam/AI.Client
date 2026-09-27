namespace AI.Web.Composer;

/// <summary>
/// Encapsulates the "user pressed Enter in the composer" flow: validating input, deciding what
/// pre-pause/auto-resume side-effects are needed for the chosen mode, calling the chat APIs in
/// the right order, and returning a typed outcome for the Razor component to act on.
///
/// Split out of Home.razor because the original logic had grown ~80 lines of branching that mixed
/// UI concerns with server orchestration, and the order-of-operations bugs (pre-pause running
/// before chat creation, queue auto-resuming, duplicate enqueues) were all the kind that unit
/// tests can pin down faster than manual runs.
///
/// The rules this service implements (which run status blocks/allows which action) are the
/// normative table in docs/15-composer-rules.md — change the doc first, then this service and
/// its tests, not the other way around.
/// </summary>
public interface IChatComposerService
{
    Task<ComposerSubmitOutcome> SubmitAsync(ComposerSubmitRequest request, CancellationToken cancellationToken);
}
