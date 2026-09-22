namespace AI.Client.Mcp.App;

using AI.Client.Application.Runs;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Runs;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>One choice offered to the person. Plain text: these are captions on controls, not content.</summary>
/// <param name="Description">A line of nuance under the label, for a choice whose consequence is not obvious.</param>
public sealed record AskUserOption(string Label, string? Description = null);

/// <param name="Id">Names this question in the answer. Must be unique within the call.</param>
/// <param name="Text">
/// What is being asked. Emphasis, code spans and links are rendered; anything larger is not, because
/// a question is a label on a decision and the decision is what the person should be reading.
/// </param>
/// <param name="Label">A short chip beside the question � "Scope", "Naming" � or nothing.</param>
/// <param name="MultiSelect">True only when the choices genuinely combine.</param>
/// <param name="AllowOther">Whether the person may type an answer of their own instead of choosing.</param>
public sealed record AskUserQuestion(
    string Id,
    string Text,
    AskUserOption[] Options,
    string? Label = null,
    bool MultiSelect = false,
    bool AllowOther = true);

/// <param name="Selected">
/// The labels the person chose, not their positions: what is stored in the transcript should still
/// say what was decided when it is read back without the question in front of it.
/// </param>
/// <param name="Other">What they typed, when they typed something instead of choosing.</param>
public sealed record AskUserReply(string Id, string[] Selected, string? Other);

/// <param name="Outcome">
/// <c>answered</c>, <c>dismissed</c> (they told you to decide), <c>expired</c> (nobody was there)
/// or <c>interrupted</c> (there was no one to ask at all).
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
                        + "' (Recommended)' to its label. Prefer one question; up to five may be asked in one call, and asking "
                        + "them together is better than one call after another. Keep option labels short and concrete, and put "
                        + "nuance in 'description'. Use 'multiSelect' only when the choices genuinely combine. The user may "
                        + "answer some questions and not others, or none at all: an absent answer means the choice is yours to "
                        + "make, never an invitation to ask again."
                });

        [McpServerTool(Name = "ask_user", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AskUserResult))]
        private async Task<CallToolResult> AskAsync(AskUserQuestion[] questions, CancellationToken cancellationToken)
        {
            if (Validate(questions) is { } invalid)
                return reply.Reply(new AskUserResult([], "invalid", "Fix the call and ask again.", invalid), isError: true);

            // A background run has nobody watching it, so it is told so at once rather than made to wait
            // out a timeout no one will interrupt. The caller that started it is the one with a user.
            if (!run.Interactive)
                return reply.Reply(new AskUserResult([], "dismissed",
                    "You are running as a background subtask; there is no user to answer you and there will not be one. "
                    + "Do what you can without this decision, and name the unresolved choice in your final answer so the "
                    + "conversation that started you can put it to the user."));

            var response = await broker().AskAsync(run,
                new UserPromptRequest(questions.Select(Question).ToArray()), Patience, cancellationToken);

            var answers = response.Answers
                .Select(answer => Reply(questions, answer))
                .OfType<AskUserReply>()
                .Where(reply => reply.Selected.Length > 0 || !string.IsNullOrWhiteSpace(reply.Other))
                .ToArray();

            return reply.Reply(new AskUserResult(answers, Outcome(response.Outcome), Guidance(response.Outcome, answers, questions)));
        }

        private static string Outcome(UserPromptOutcome outcome) => outcome switch
        {
            UserPromptOutcome.Answered => "answered",
            UserPromptOutcome.Dismissed => "dismissed",
            UserPromptOutcome.Expired => "expired",
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
            _ => "The user declined to decide. " + DecideYourself
        };

        private static UserPromptQuestion Question(AskUserQuestion question) => new(
            question.Id,
            question.Text,
            question.Label,
            question.Options.Select(option => new UserPromptOption(option.Label, option.Description)).ToArray(),
            question.MultiSelect,
            question.AllowOther);

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
                .Where(index => index >= 0 && index < question.Options.Length)
                .Select(index => question.Options[index].Label)
                .ToArray();
            return new AskUserReply(question.Id, selected,
                string.IsNullOrWhiteSpace(answer.Other) ? null : answer.Other.Trim());
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
                var options = question.Options ?? [];
                if (options.Length > MaxOptions) return $"Question '{question.Id}' offers more than {MaxOptions} options.";
                if (options.Length == 0 && !question.AllowOther)
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
