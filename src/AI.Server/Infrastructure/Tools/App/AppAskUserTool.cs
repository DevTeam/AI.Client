namespace AI.Mcp.App;

using AI.Application.Runs;
using AI.Application.Tools;
using AI.Contracts.Runs;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>One choice offered to the person. Plain text: these are captions on controls, not content.</summary>
/// <param name="Description">A line of nuance under the label, for a choice whose consequence is not obvious.</param>
public sealed record AskUserOption(string Label, string? Description = null, bool Recommended = false);

/// <param name="Id">Names this question in the answer. Must be unique within the call.</param>
/// <param name="Text">
/// What is being asked. Emphasis, code spans and links are rendered; anything larger is not, because
/// a question is a label on a decision and the decision is what the person should be reading.
/// </param>
/// <param name="Label">A short chip beside the question — "Scope", "Naming" — or nothing.</param>
/// <param name="MultiSelect">True only when the choices genuinely combine.</param>
/// <param name="AllowOther">Whether the person may type an answer of their own instead of choosing.</param>
/// <param name="PathKind">
/// Set to "directory", "directories" or "file" when the answer is a path on the user's machine. They then get a
/// browser over the real file system instead of a text box, and the path comes back in 'other'.
/// "directories" permits several choices, which come back in 'paths'.
/// Leave it out for every other kind of question.
/// </param>
/// <param name="PickerKind">"branch" or "commit" for Git selection; use MultiSelect for several choices.</param>
/// <param name="RepositoryPath">Absolute repository directory on the host, required for a Git picker.</param>
/// <param name="Revision">Optional branch or revision limiting the commit history.</param>
public sealed record AskUserQuestion(
    string Id,
    string Text,
    AskUserOption[] Options,
    string? Label = null,
    bool MultiSelect = false,
    bool AllowOther = true,
    string? PathKind = null,
    string? PickerKind = null,
    string? RepositoryPath = null,
    string? Revision = null);

/// <param name="Selected">
/// The labels the person chose, not their positions: what is stored in the transcript should still
/// say what was decided when it is read back without the question in front of it.
/// </param>
/// <param name="Other">What they typed, when they typed something instead of choosing.</param>
public sealed record AskUserReply(string Id, string[] Selected, string? Other, IReadOnlyList<string>? Paths = null,
    IReadOnlyList<string>? Values = null);

/// <param name="Outcome">
/// <c>answered</c>, <c>dismissed</c> (they told you to decide), <c>declined</c> (they refused the
/// question and want you to stop), <c>expired</c> (nobody was there) or <c>interrupted</c> (there was
/// no one to ask at all).
/// </param>
/// <param name="Guidance">What to do about that outcome, in the words the model should act on.</param>
public sealed record AskUserResult(
    AskUserReply[] Answers,
    string Outcome,
    string Guidance,
    string? Error = null);

/// <summary>
/// Puts a question to the person and waits for their answer.
/// </summary>
/// <remarks>
/// The tool is an ordinary MCP tool with an ordinary schema; what is unusual is only that its call
/// blocks until somebody decides. It reaches the person through the run it was opened for, which
/// the session hands it at construction — not through a chat id the model names, which would let a
/// model interrupt a conversation it is not part of.
///
/// Unanswered is a first-class result, not a failure. Someone who walks away, declines to decide,
/// or is not there at all leaves the model exactly where it was before it asked: free to choose and
/// obliged to say what it chose. That is why nothing here returns an error for silence.
/// </remarks>
[McpServerToolType]
public sealed class AppAskUserTool(Func<IUserPromptBroker> broker) : IAppTool
{
    /// <summary>
    /// The tool is built per session and the application registers one instance of this class, so
    /// the run cannot be kept here: two chats asking at once would each answer into the other's.
    /// Each session gets its own <see cref="Session"/> holding its own run, and the shared instance
    /// holds nothing but the way to reach the broker.
    /// </summary>
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(broker, run, reply).Create();

    private sealed class Session(Func<IUserPromptBroker> broker, ToolRunContext run, IAppToolReply reply)
    {
        /// <summary>
        /// How long a question waits. Long enough to fetch a coffee and come back, short enough that a
        /// run abandoned overnight is not still holding a tool call open in the morning.
        /// </summary>
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(15);

        private const int MaxQuestions = 5;
        private const int MaxOptions = 8;
        private const int MaxTextLength = 500;
        private const int MaxLabelLength = 24;
        private const int MaxOptionLength = 80;
        private const int MaxDescriptionLength = 160;

        private const string DirectoryPath = "directory";
        private const string DirectoriesPath = "directories";
        private const string FilePath = "file";

        private const string DecideYourself =
            "There is no answer. Continue with the option you judge best and state the assumption you made "
            + "in your reply. Do not ask the same question again.";

        public McpServerTool Create() =>
            McpServerTool.Create(
                AskAsync,
                new McpServerToolCreateOptions
                {
                    SerializerOptions = reply.Json,
                    Description =
                        "Ask the user a multiple-choice question and wait for their answer. Use it only when the request is "
                        + "genuinely ambiguous and guessing wrong would waste real work, when a decision has visible trade-offs "
                        + "the user owns — target version, scope of a refactor, naming, where new code goes — or when a parameter "
                        + "cannot be inferred from the project at all. If the task is concrete enough to do, do it and state your "
                        + "assumption in the reply instead of asking. Put the option you recommend first and append "
                        + "' (Recommended)' to its label, translated like every label into the user's language (' (рекомендуется)' "
                        + "in Russian). Prefer one question; up to five may be asked in one call, and asking "
                        + "them together is better than one call after another. Keep option labels short and concrete, and put "
                        + "nuance in 'description'. When the answer is a path on the user's machine, set 'pathKind' to "
                        + "'directory', 'directories' or 'file': the user then picks it out of their own file system. A single path comes back in "
                        + "'other'; 'directories' allows several selections and returns them in 'paths'. This is far more reliable than asking them to type paths. Options may still be offered "
                        + "alongside — list the paths you already consider likely. Use 'multiSelect' only when the choices "
                        + "genuinely combine. For Git selection set 'pickerKind' to 'branch' or 'commit' and 'repositoryPath' to the absolute repository directory. "
                        + "Set 'multiSelect' to true for several branches or commits. Optional 'revision' limits commit history to that branch or revision. "
                        + "Leave 'pathKind' unset; options may be empty. The Git picker returns full ref names or commit hashes in 'values', in selection order. The user may "
                        + "answer some questions and not others, or none at all: an absent answer means the choice is yours to "
                        + "make, never an invitation to ask again. The user may also decline the question outright "
                        + "(outcome 'declined'): then stop that work and wait for them instead of choosing. "
                        + "presentation='overlay' asks outside the chat, including from a hidden/background chat; by default it expires "
                        + "and cancels the dependent action if unanswered. timeoutSeconds is 5..120 for overlays. "
                        + "In an already started application guide, opening interests and questions within an unfinished topic "
                        + "must explicitly use timeoutSeconds=0 with timeoutBehavior='cancel': wait without countdown or expiry. "
                        + "Recommend useful continuation, not Stop. A timed cancelling learning question belongs only at an important "
                        + "topic fork after the current branch and takeaway are complete; Finish may be recommended there. "
                        + "Automatic invitations must still expire. "
                        + "Mark recommended options with recommended=true. timeoutBehavior='submit_defaults' displays a countdown "
                        + "only when every question has explicit recommended options; use it for choices within a guide the user started, "
                        + "never to start a guide, change settings, send messages or create objects. Editing stops automatic submission."
                });

        [McpServerTool(Name = "ask_user", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AskUserResult))]
        private async Task<CallToolResult> AskAsync(AskUserQuestion[] questions, string presentation = "chat",
            int? timeoutSeconds = null, string timeoutBehavior = "cancel", CancellationToken cancellationToken = default)
        {
            if (run.IsGuide) presentation = "overlay";
            if (Validate(questions) is { } invalid)
                return reply.Reply(new AskUserResult([], "invalid", "Fix the call and ask again.", invalid), isError: true);
            if (presentation is not ("chat" or "overlay") || timeoutBehavior is not ("cancel" or "submit_defaults")
                || ((timeoutSeconds is < 5 or > 900) && !(run.IsGuide && timeoutSeconds == 0 && timeoutBehavior == "cancel"))
                || presentation == "overlay" && timeoutSeconds is > 120)
                return reply.Reply(new AskUserResult([], "invalid", "Fix the presentation, timeout or timeout behavior."), true);
            var submitDefaults = timeoutBehavior == "submit_defaults";
            if (submitDefaults && questions.Any(question => !question.Options.Any(option => option.Recommended)
                || !question.MultiSelect && question.Options.Count(option => option.Recommended) != 1
                || question.PathKind is not null || question.PickerKind is not null))
                return reply.Reply(new AskUserResult([], "invalid", "Automatic answers require explicit recommended options for every question."), true);

            // A background run has nobody watching it, so it is told so at once rather than made to wait
            // out a timeout no one will interrupt. The caller that started it is the one with a user.
            if (!run.Interactive && presentation != "overlay")
                return reply.Reply(new AskUserResult([], "dismissed",
                    "You are running as a background subtask; there is no user to answer you and there will not be one. "
                    + "Do what you can without this decision, and name the unresolved choice in your final answer so the "
                    + "conversation that started you can put it to the user."));

            var response = await broker().AskAsync(run,
                new UserPromptRequest(questions.Select(Question).ToArray(), presentation, submitDefaults),
                timeoutSeconds == 0 ? Timeout.InfiniteTimeSpan
                    : timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds)
                    : presentation == "overlay" || submitDefaults ? TimeSpan.FromSeconds(30) : Patience, cancellationToken);

            var answers = response.Answers
                .Select(answer => Reply(questions, answer))
                .OfType<AskUserReply>()
                .Where(reply => reply.Selected.Length > 0 || !string.IsNullOrWhiteSpace(reply.Other) || reply.Paths is { Count: > 0 } || reply.Values is { Count: > 0 })
                .ToArray();

            var guidance = presentation == "overlay" && response.Outcome is not UserPromptOutcome.Answered
                ? "The outside-chat question was cancelled or unanswered. Stop the dependent action and this guide; do not choose defaults or ask again."
                : Guidance(response.Outcome, answers, questions);
            return reply.Reply(new AskUserResult(answers, Outcome(response.Outcome), guidance));
        }

        private static string Outcome(UserPromptOutcome outcome) => outcome switch
        {
            UserPromptOutcome.Answered => "answered",
            UserPromptOutcome.Dismissed => "dismissed",
            UserPromptOutcome.Expired => "expired",
            UserPromptOutcome.Declined => "declined",
            _ => "interrupted"
        };

        private static string Guidance(UserPromptOutcome outcome, AskUserReply[] answers, AskUserQuestion[] questions) => outcome switch
        {
            UserPromptOutcome.Answered when answers.Length == questions.Length =>
                "Proceed on these answers.",
            // Partial answers are the common case, not an edge one: a person answers what they care
            // about. Saying which questions came back empty is what stops the model asking them again.
            UserPromptOutcome.Answered =>
                "Proceed on the answers given. These were left to you: "
                + string.Join(", ", questions.Select(question => question.Id)
                    .Except(answers.Select(answer => answer.Id), StringComparer.Ordinal))
                + ". " + DecideYourself,
            UserPromptOutcome.Expired => "Nobody answered in time. " + DecideYourself,
            // Unlike dismissing, declining hands nothing over: choosing for them anyway is exactly
            // what they just refused, so the only thing left to do is stop and listen.
            UserPromptOutcome.Declined =>
                "The user declined to answer and does not want you to choose for them. Do not proceed with the work "
                        + "this question was about and do not ask it again. End your turn with a short reply that says what "
                + "you stopped and wait for the user's next message.",
            _ => "The user declined to decide. " + DecideYourself
        };

        private static UserPromptQuestion Question(AskUserQuestion question) => new(
            question.Id,
            question.Text,
            question.Label,
            (question.Options ?? []).Select(option => new UserPromptOption(option.Label, option.Description, option.Recommended)).ToArray(),
            question.MultiSelect,
            question.AllowOther,
            question.PathKind?.Trim().ToLowerInvariant(),
            question.PickerKind?.Trim().ToLowerInvariant(),
            question.RepositoryPath?.Trim(),
            question.Revision);

        /// <summary>
        /// Turns positions back into labels. An answer naming an option that no longer exists is
        /// dropped rather than guessed at: the two sides of this conversion are a process apart, and a
        /// mismatch means the question was replaced, not that the person meant the neighbouring choice.
        /// </summary>
        private static AskUserReply? Reply(AskUserQuestion[] questions, UserPromptAnswer answer)
        {
            var question = questions.FirstOrDefault(item => string.Equals(item.Id, answer.QuestionId, StringComparison.Ordinal));
            if (question is null) return null;
            var selected = answer.Selected
                .Where(index => index >= 0 && index < (question.Options?.Length ?? 0))
                .Select(index => question.Options![index].Label)
                .ToArray();
            var paths = question.PathKind?.Trim().Equals(DirectoriesPath, StringComparison.OrdinalIgnoreCase) == true
                ? answer.Paths?.Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : null;
            var values = question.PickerKind?.Trim().ToLowerInvariant() is "branch" or "commit"
                ? answer.Values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                    .Distinct(StringComparer.Ordinal).Take(question.MultiSelect ? 200 : 1).ToArray()
                : null;
            return new AskUserReply(question.Id, selected,
                string.IsNullOrWhiteSpace(answer.Other) ? null : answer.Other.Trim(),
                paths is { Length: > 0 } ? paths : null,
                values is { Length: > 0 } ? values : null);
        }

        /// <summary>
        /// Says what is wrong in terms the model can act on, because it is the one that will fix it.
        /// Every limit here is about the card staying readable: a question nobody can take in at a
        /// glance is worse than no question at all.
        /// </summary>
        private static string? Validate(AskUserQuestion[]? questions)
        {
            if (questions is not { Length: > 0 }) return "Ask at least one question.";
            if (questions.Length > MaxQuestions) return $"Ask at most {MaxQuestions} questions in one call.";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var question in questions)
            {
                if (string.IsNullOrWhiteSpace(question.Id)) return "Every question needs a non-empty 'id'.";
                if (!ids.Add(question.Id)) return $"Question ids must be unique; '{question.Id}' is repeated.";
                if (string.IsNullOrWhiteSpace(question.Text)) return $"Question '{question.Id}' has no text.";
                if (question.Text.Length > MaxTextLength) return $"Question '{question.Id}' is longer than {MaxTextLength} characters.";
                if (question.Label is { Length: > MaxLabelLength }) return $"The label of '{question.Id}' is longer than {MaxLabelLength} characters.";
                var path = question.PathKind?.Trim().ToLowerInvariant();
                if (path is { Length: > 0 } and not (DirectoryPath or DirectoriesPath or FilePath))
                    return $"The 'pathKind' of '{question.Id}' must be '{DirectoryPath}', '{DirectoriesPath}' or '{FilePath}'.";
                var options = question.Options ?? [];
                var picker = question.PickerKind?.Trim().ToLowerInvariant();
                if (picker is { Length: > 0 })
                {
                    if (picker is not ("branch" or "commit")) return $"The 'pickerKind' of '{question.Id}' must be 'branch' or 'commit'.";
                    if (path is { Length: > 0 }) return $"Question '{question.Id}' cannot combine 'pathKind' and 'pickerKind'.";
                    if (string.IsNullOrWhiteSpace(question.RepositoryPath) || !Path.IsPathFullyQualified(question.RepositoryPath))
                        return $"Question '{question.Id}' needs an absolute 'repositoryPath' for its Git picker.";
                }
                if (options.Length > MaxOptions) return $"Question '{question.Id}' offers more than {MaxOptions} options.";
                // A path question is answerable through its picker, so it needs neither options nor
                // the free-text box the other kinds fall back on.
                if (options.Length == 0 && !question.AllowOther && path is not { Length: > 0 } && picker is not { Length: > 0 })
                    return $"Question '{question.Id}' offers no options and no free-text answer, so it cannot be answered.";
                foreach (var option in options)
                {
                    if (string.IsNullOrWhiteSpace(option.Label)) return $"Question '{question.Id}' has an option with no label.";
                    if (option.Label.Length > MaxOptionLength) return $"An option of '{question.Id}' is longer than {MaxOptionLength} characters.";
                    if (option.Description is { Length: > MaxDescriptionLength })
                        return $"An option description of '{question.Id}' is longer than {MaxDescriptionLength} characters.";
                }
            }

            return null;
        }
    }
}
