namespace AI.Application.Chat;

using Tools;

/// <summary>How long a model-only instruction remains active inside one run.</summary>
public enum ModelInstructionLifetime
{
    Request,
    UntilAcknowledged,
    Run
}

/// <summary>
/// Where an instruction sits in the system preamble. Standing layers — the base prompt, project
/// instructions and memory — change rarely, so they come first and keep the request prefix stable
/// for provider-side prompt caching; each is bounded by its own budget where it is built. Run
/// instructions follow and share the composer's run budget. Trailing instructions change from step
/// to step and go after the conversation, where a change costs no cached prefix; an instruction
/// whose lifetime is shorter than the run is always trailing.
/// </summary>
public enum ModelInstructionPlacement
{
    Trailing = -1,
    Run = 0,
    Standing = 1
}

/// <summary>
/// A trusted instruction addressed only to the model. The key is stable identity used for
/// replacement, acknowledgement and diagnostics; the content is never part of chat persistence.
/// </summary>
public sealed record ModelInstruction(
    string Key,
    string Content,
    int Priority = 0,
    ModelInstructionLifetime Lifetime = ModelInstructionLifetime.Run,
    ModelInstructionPlacement Placement = ModelInstructionPlacement.Run);

/// <summary>
/// Run-local mailbox for application mechanisms which need to guide the next model request
/// without manufacturing user messages or exposing implementation protocol in the transcript.
/// </summary>
public interface IModelInstructionRegistry
{
    IDisposable Begin(ToolRunContext run);
    void Upsert(ToolRunContext run, ModelInstruction instruction);
    void Remove(ToolRunContext run, string key);
    IReadOnlyList<ModelInstruction> List(ToolRunContext run);
    void Acknowledge(ToolRunContext run, IReadOnlyCollection<string> keys);
}
