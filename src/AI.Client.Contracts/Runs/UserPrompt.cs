namespace AI.Client.Contracts.Runs;

/// <summary>
/// A question the model asked the person to answer, waiting in the run that asked it.
/// </summary>
/// <remarks>
/// Deliberately shaped like <see cref="ToolApproval"/>: both are "the turn has stopped and only a
/// person can restart it", both live in the run snapshot rather than in storage, and both are
/// answered through the dispatcher. What differs is who the question belongs to — an approval is
/// the Host asking permission for something it is about to do, a prompt is the model asking the
/// person to choose — so the two are never merged into one card.
/// </remarks>
/// <param name="TimeoutSeconds">
/// How long the prompt stays answerable. Reaching it is not a failure: the run continues with
/// <see cref="UserPromptOutcome.Expired"/>, because a person who walked away has not refused.
/// </param>
public sealed record UserPrompt(
    Guid Id,
    IReadOnlyList<UserPromptQuestion> Questions,
    long TimeoutSeconds);

/// <param name="Text">
/// Question text. An inline subset of markdown is rendered — emphasis, code spans, links — because
/// questions name files and identifiers. Block content is not: a question is a label, not a page.
/// </param>
/// <param name="Label">A short chip shown beside the question, or null.</param>
/// <param name="AllowOther">Whether a free-text answer is offered alongside the options.</param>
/// <param name="PathKind">
/// "directory" or "file" when the answer is a path on this machine, and null when it is not. The
/// free-text answer then becomes a path box with a picker behind it: the person browses the host's
/// own file system, because a path typed from memory is the thing they get wrong. Options may still
/// be offered alongside it — likely paths are worth one click — and the chosen path comes back in
/// <see cref="UserPromptAnswer.Other"/> like any other typed answer.
/// </param>
public sealed record UserPromptQuestion(
    string Id,
    string Text,
    string? Label,
    IReadOnlyList<UserPromptOption> Options,
    bool MultiSelect,
    bool AllowOther,
    string? PathKind = null);

/// <summary>One choice. Plain text in both fields: these are captions on controls, never markup.</summary>
public sealed record UserPromptOption(string Label, string? Description);

/// <summary>
/// What the person chose for one question. Options are identified by position, so the answer cannot
/// name a choice that was never offered; the tool turns them back into their labels before the
/// model sees them, which is what leaves a readable trace in the transcript.
/// </summary>
/// <param name="Selected">Indices into the question's options. Empty means the question was left to the model.</param>
/// <param name="Other">Free text, when the question allowed it and the person typed some.</param>
public sealed record UserPromptAnswer(string QuestionId, IReadOnlyList<int> Selected, string? Other);

public enum UserPromptOutcome
{
    Answered,

    /// <summary>The person declined to decide and told the model to choose for itself.</summary>
    Dismissed,

    /// <summary>Nobody answered in time. The run continues; the question was not refused.</summary>
    Expired,

    /// <summary>The run was stopped, or there was no interactive surface to ask on at all.</summary>
    Interrupted
}

/// <summary>
/// An answer on its way back to the run. Questions left unanswered are simply absent, which the
/// model is told means "your call": a person answering the one question they care about must not
/// have to invent answers for the rest.
/// </summary>
public sealed record UserPromptResponse(
    Guid PromptId,
    UserPromptOutcome Outcome,
    IReadOnlyList<UserPromptAnswer> Answers);
