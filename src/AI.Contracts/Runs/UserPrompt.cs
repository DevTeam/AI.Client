namespace AI.Contracts.Runs;

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
/// A nonpositive value with no ExpiresAt means it waits for an answer or cancellation without a timer.
/// </param>
/// <param name="ReplyButtons">
/// The single question's options are drawn as reply buttons: pressing one is the answer, so the card
/// has no Answer button. Only a single-choice question with plain options is asked this way.
/// </param>
public sealed record UserPrompt(
    Guid Id,
    IReadOnlyList<UserPromptQuestion> Questions,
    long TimeoutSeconds,
    string Presentation = "chat", bool SubmitDefaults = false, DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? DefaultSubmitAt = null, bool ReplyButtons = false);

/// <param name="Text">
/// Question text. An inline subset of markdown is rendered — emphasis, code spans, links — because
/// questions name files and identifiers. Block content is not: a question is a label, not a page.
/// </param>
/// <param name="Label">A short chip shown beside the question, or null.</param>
/// <param name="AllowOther">Whether a free-text answer is offered alongside the options.</param>
/// <param name="PathKind">
/// "directory", "directories" or "file" when the answer is a path on this machine, and null when it is not. The
/// free-text answer then becomes a path box with a picker behind it: the person browses the host's
/// own file system, because a path typed from memory is the thing they get wrong. Options may still
/// be offered alongside it — likely paths are worth one click — and the chosen path comes back in
/// <see cref="UserPromptAnswer.Other"/> like any other typed answer. The "directories" mode allows
/// several selections and returns them in <see cref="UserPromptAnswer.Paths"/>.
/// </param>
/// <param name="PickerKind">
/// "branch" or "commit" for a Git picker; "date", "time" or "recurrence" for a schedule picker.
/// MultiSelect allows several choices. Every picker answers in <see cref="UserPromptAnswer.Values"/>.
/// </param>
/// <param name="RepositoryPath">Absolute repository directory on the host for a Git picker.</param>
/// <param name="Revision">Optional branch or revision to limit the commit history.</param>
/// <param name="Input">
/// The control a value is entered with — a slider, a scale, an order, a searchable list, a colour or
/// a text box — or null for options, paths and pickers. See <see cref="UserPromptInputKind"/>.
/// </param>
/// <param name="Defaults">
/// What the question holds before anyone touches it, in the spelling its answer comes back in: a
/// number, the two ends of a range, a colour, text, picked values or paths. It is also what the
/// model goes on with when the question is left to it. Recommended options are the default of a
/// choice and are not repeated here.
/// </param>
/// <param name="Preview">
/// A line shown under the control that follows the answer as it changes — "≈ {value * 12} files" —
/// so the consequence is read before it is sent. See <see cref="IUserPromptInputs.RenderPreview"/>.
/// </param>
/// <param name="Rememberable">
/// The answer is a lasting preference; the card offers "Remember my answer", and the model is
/// asked to save what the person chose instead of asking again next time.
/// </param>
public sealed record UserPromptQuestion(
    string Id,
    string Text,
    string? Label,
    IReadOnlyList<UserPromptOption> Options,
    bool MultiSelect,
    bool AllowOther,
    string? PathKind = null,
    string? PickerKind = null,
    string? RepositoryPath = null,
    string? Revision = null,
    UserPromptInput? Input = null,
    IReadOnlyList<string>? Defaults = null,
    string? Preview = null,
    bool Rememberable = false);

/// <summary>One choice. Plain text in both fields: these are captions on controls, never markup.</summary>
/// <param name="Value">
/// What choosing it means to a program — a date, a time, a recurrence or any other exact value.
/// A chosen option's value is returned in <see cref="UserPromptAnswer.Values"/> next to its label.
/// </param>
public sealed record UserPromptOption(string Label, string? Description, bool Recommended = false, string? Value = null);

/// <summary>
/// What the person chose for one question. Options are identified by position, so the answer cannot
/// name a choice that was never offered; the tool turns them back into their labels before the
/// model sees them, which is what leaves a readable trace in the transcript.
/// </summary>
/// <param name="Selected">
/// Indices into the question's options. Empty means the question was left to the model. For a
/// rank question they are every option, in the order the person put them.
/// </param>
/// <param name="Other">Free text, or one selected path.</param>
/// <param name="Paths">Several selected directory paths for a "directories" question.</param>
/// <param name="Values">
/// Picked values in selection order: full Git ref names or commit hashes, dates (yyyy-MM-dd), times
/// (HH:mm) or recurrences (JSON), and the values of chosen options. A number or scale answers with
/// one number, a range with its two ends and a colour with <c>#rrggbb</c>, all in invariant spelling.
/// </param>
/// <param name="Remember">The person ticked "Remember my answer" on a rememberable question.</param>
public sealed record UserPromptAnswer(
    string QuestionId,
    IReadOnlyList<int> Selected,
    string? Other,
    IReadOnlyList<string>? Paths = null,
    IReadOnlyList<string>? Values = null,
    bool Remember = false);

public enum UserPromptOutcome
{
    Answered,

    /// <summary>The person declined to decide and told the model to choose for itself.</summary>
    Dismissed,

    /// <summary>Nobody answered in time. The run continues; the question was not refused.</summary>
    Expired,

    /// <summary>The run was stopped, or there was no interactive surface to ask on at all.</summary>
    Interrupted,

    /// <summary>
    /// The person refused the question itself: they do not want the model to choose either, so the
    /// work the question was about stops here and waits for what they say next.
    /// </summary>
    Declined
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
