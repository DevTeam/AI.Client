namespace AI.Mcp.App;

using AI.Application.Runs;
using AI.Application.Chats;
using AI.Domain.Chats;
using AI.Application.Tools;
using AI.Contracts.Runs;
using AI.Contracts.Schedules;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>One choice offered to the person. Plain text: these are captions on controls, not content.</summary>
/// <param name="Description">A line of nuance under the label, for a choice whose consequence is not obvious.</param>
/// <param name="Value">
/// The exact value the choice stands for, returned in 'values' when it is chosen. For a date, time or
/// recurrence picker it must be a value that picker returns.
/// </param>
public sealed record AskUserOption(string Label, string? Description = null, bool Recommended = false, string? Value = null);

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
/// <param name="PickerKind">
/// "branch" or "commit" for Git selection; "date", "time" or "recurrence" for schedule values. Use
/// MultiSelect for several choices.
/// </param>
/// <param name="RepositoryPath">Absolute repository directory on the host, required for a Git picker.</param>
/// <param name="Revision">Optional branch or revision limiting the commit history.</param>
/// <param name="Input">
/// The control for a value rather than a choice: a slider, a scale, an order, a searchable list, a
/// colour or a text box. Leave it out for options, paths and pickers.
/// </param>
/// <param name="Default">
/// What the control holds before anyone touches it — a number, [from, to], '#rrggbb', text, a date, a
/// time, a recurrence, a ref or a path — and what applies when the question is left to you.
/// Recommended options are the default of a choice.
/// </param>
/// <param name="Preview">
/// A line under the control that follows the answer as it changes, such as '≈ {value * 12} files'.
/// Names: value, from, to, count, label, text; '{expression:N}' shows N decimals.
/// </param>
/// <param name="Rememberable">
/// True when the answer is a lasting preference worth not asking again; the user can then tick
/// "Remember my answer".
/// </param>
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
    string? Revision = null,
    AskUserInput? Input = null,
    JsonElement? Default = null,
    string? Preview = null,
    bool Rememberable = false);

/// <param name="Kind">
/// 'number' (one value on a slider), 'range' (two ends on a slider), 'scale' (a rating, 1..5 by
/// default), 'rank' (the user orders the options), 'search' (filter a long list of options),
/// 'color' (options are swatches with a '#rrggbb' value) or 'text' (a text, multiline or code box).
/// </param>
/// <param name="Min">Low end of a number, range or scale.</param>
/// <param name="Max">High end of a number, range or scale.</param>
/// <param name="Step">Slider step; chosen from the span when left out.</param>
/// <param name="Unit">Short unit shown with the value: 'ms', '%', 'files'.</param>
/// <param name="Marks">Labelled points on a slider or scale.</param>
/// <param name="Multiline">A text answer of several lines.</param>
/// <param name="Language">The text is code or configuration in this language and is edited in monospace.</param>
/// <param name="Pattern">A regular expression the whole text must match; the user cannot send text that does not.</param>
/// <param name="Placeholder">A hint in an empty text or search box.</param>
/// <param name="MaxLength">The longest text accepted; 4000 when left out.</param>
public sealed record AskUserInput(
    string Kind,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string? Unit = null,
    AskUserMark[]? Marks = null,
    bool Multiline = false,
    string? Language = null,
    string? Pattern = null,
    string? Placeholder = null,
    int? MaxLength = null);

/// <summary>A labelled point on a slider or scale.</summary>
public sealed record AskUserMark(double Value, string Label);

/// <summary>The two ends a range answer chose.</summary>
public sealed record AskUserRange(double From, double To);

/// <param name="Selected">
/// The labels the person chose, not their positions: what is stored in the transcript should still
/// say what was decided when it is read back without the question in front of it.
/// </param>
/// <param name="Other">What they typed, when they typed something instead of choosing.</param>
/// <param name="Values">Exact values in selection order: picked Git refs, commits, dates, times or recurrences, and the values of chosen options.</param>
/// <param name="ValueDescriptions">
/// The answer in words, to say back to the user: each recurrence in 'values', or a number, range or
/// scale with its unit and mark.
/// </param>
/// <param name="Number">The number a 'number' or 'scale' question was answered with.</param>
/// <param name="Range">The two ends a 'range' question was answered with.</param>
/// <param name="Ranked">'selected' holds every option of a 'rank' question, first to last.</param>
/// <param name="Assumed">
/// The user left this question to you, and its default — your recommended options or 'default' —
/// was taken and shown to them as your choice.
/// </param>
/// <param name="Remember">The user asked you to remember this answer.</param>
public sealed record AskUserReply(string Id, string[] Selected, string? Other, IReadOnlyList<string>? Paths = null,
    IReadOnlyList<string>? Values = null, IReadOnlyList<string>? ValueDescriptions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Number = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AskUserRange? Range = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Ranked = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Assumed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Remember = null);

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
public sealed class AppAskUserTool(Func<IUserPromptBroker> broker, IChatKindPolicyRegistry kindPolicies,
    IScheduleCalendar calendar, IScheduleDescriptions descriptions, IUserPromptInputs inputs) : IAppTool
{
    /// <summary>
    /// The tool is built per session and the application registers one instance of this class, so
    /// the run cannot be kept here: two chats asking at once would each answer into the other's.
    /// Each session gets its own <see cref="Session"/> holding its own run, and the shared instance
    /// holds nothing but the way to reach the broker.
    /// </summary>
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(broker, kindPolicies, calendar, descriptions, inputs, run, reply).Create();

    private sealed class Session(Func<IUserPromptBroker> broker, IChatKindPolicyRegistry kindPolicies,
        IScheduleCalendar calendar, IScheduleDescriptions descriptions, IUserPromptInputs inputs, ToolRunContext run, IAppToolReply reply)
    {
        /// <summary>
        /// How long a question waits. Long enough to fetch a coffee and come back, short enough that a
        /// run abandoned overnight is not still holding a tool call open in the morning.
        /// </summary>
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(15);

        private const int MaxQuestions = 5;
        private const int MaxTextLength = 500;
        private const int MaxLabelLength = 24;
        private const int MaxOptionLength = 80;
        private const int MaxDescriptionLength = 160;
        private const int MaxValueLength = 2000;
        private const int MinReplyButtons = 2;
        private const int MaxReplyButtons = 6;

        private const string DirectoryPath = "directory";
        private const string DirectoriesPath = "directories";
        private const string FilePath = "file";

        private const string DecideYourself =
            "Continue with the option you judge best and state the assumption you made in your reply. "
            + "Do not ask the same question again.";

        public McpServerTool Create() =>
            McpServerTool.Create(
                AskAsync,
                new McpServerToolCreateOptions
                {
                    SerializerOptions = reply.Json,
                    Description =
                        "Ask the user a question and wait for their answer. Use it only when the request is "
                        + "genuinely ambiguous and guessing wrong would waste real work, when a decision has visible trade-offs "
                        + "the user owns — target version, scope of a refactor, naming, where new code goes — or when a parameter "
                        + "cannot be inferred from the project at all. If the task is concrete enough to do, do it and state your "
                        + "assumption in the reply instead of asking. Put the option you recommend first and append "
                        // A translated example here read as a hint about the language: models answered
                        // English requests with Russian labels and replies once it was in the schema.
                        + "' (Recommended)' to its label, translated like every label into the language the user writes in. "
                        + "Prefer one question; up to five may be asked in one call, and asking "
                        + "them together is better than one call after another. Keep option labels short and concrete, and put "
                        + "nuance in 'description'. "
                        + "DEFAULTS: give every question the answer you would choose — recommended=true on options, or 'default' for "
                        + "an input, picker, path or free-text question. A question the user leaves to you takes that default: it is "
                        + "returned with assumed=true and shown to the user as your choice. "
                        + "VALUES: for a value rather than a choice set 'input' {kind, ...}: "
                        + "'number' {min, max, step?, unit?, marks?} is a slider, answer in 'number'; "
                        + "'range' {min, max, step?, unit?, marks?} a two-handle slider, answer in 'range' {from, to}, default [from, to]; "
                        + "'scale' {min?=1, max?=5, marks?} a rating, answer in 'number'; "
                        + "'rank' the user orders the options (list them in the order you recommend), answer in 'selected' first to last; "
                        + "'search' a filterable list of up to 200 options (recommended=true for the default); "
                        + "'color' a colour picker whose options are swatches with value '#rrggbb', answer in 'values'; "
                        + "'text' {multiline?, language?, pattern?, placeholder?, maxLength?} a text or code box, answer in 'other'. "
                        + "Options of a number or range are presets with 'value' '42' or '10..20'. 'valueDescriptions' says a "
                        + "number, range or scale back in words with its unit. 'preview' is a line shown live under the control, "
                        + "such as '≈ {value * 12} files' (names: value, from, to, count, label, text; '{expression:N}' for N decimals). "
                        + "Set 'rememberable' when the answer is a lasting preference; when the result says remember=true, save that "
                        + "answer with app_memory (the memory-save playbook) so it is not asked again. "
                        + "PATHS: when the answer is a path on the user's machine, set 'pathKind' to "
                        + "'directory', 'directories' or 'file': the user then picks it out of their own file system. A single path comes back in "
                        + "'other'; 'directories' allows several selections and returns them in 'paths'. This is far more reliable than asking them to type paths. Options may still be offered "
                        + "alongside — list the paths you already consider likely. Use 'multiSelect' only when the choices "
                        + "genuinely combine. "
                        + "PICKERS: for Git selection set 'pickerKind' to 'branch' or 'commit' and 'repositoryPath' to the absolute repository directory. "
                        + "Set 'multiSelect' to true for several branches or commits. Optional 'revision' limits commit history to that branch or revision. "
                        + "Leave 'pathKind' unset; options may be empty. The Git picker returns full ref names or commit hashes in 'values', in selection order. "
                        + "For a date, a time of day or a repeating schedule set 'pickerKind' to 'date', 'time' or 'recurrence' (no repositoryPath): "
                        + "the user picks on a calendar, a clock or a recurrence editor, and 'values' returns exact 'yyyy-MM-dd', 'HH:mm' or recurrence JSON "
                        + "(the shape app_schedule takes), with 'valueDescriptions' saying each recurrence in words. Options may offer presets with an exact "
                        + "'value' in that format, such as {label:'Tomorrow', value:'2026-10-08'}; any option may carry a 'value', returned in 'values' when chosen. "
                        + "Ask for the missing parts only: never a date or time the user already gave. "
                        + "OUTCOMES: the user may answer some questions and not others, or none at all: an absent answer means the choice is yours to "
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
                        + "only when every question has a recommended option or a 'default'; use it for choices within a guide the user started, "
                        + "never to start a guide, change settings, send messages or create objects. Editing stops automatic submission. "
                        + "answerStyle='buttons' sends one click as the answer: for one single-choice question with 2-6 short options."
                });

        [McpServerTool(Name = "ask_user", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AskUserResult))]
        private async Task<CallToolResult> AskAsync(AskUserQuestion[] questions, string presentation = "chat",
            int? timeoutSeconds = null, string timeoutBehavior = "cancel", string answerStyle = "form",
            CancellationToken cancellationToken = default)
        {
            var behavior = kindPolicies.Resolve(run.Kind == default ? ChatKind.Conversation : run.Kind).Behavior;
            if (behavior.ForceOverlayQuestions) presentation = "overlay";
            if (Validate(questions) is { } invalid)
                return reply.Reply(new AskUserResult([], "invalid", "Fix the call and ask again.", invalid), isError: true);
            if (presentation is not ("chat" or "overlay") || timeoutBehavior is not ("cancel" or "submit_defaults")
                || ((timeoutSeconds is < 5 or > 900) && !(behavior.AllowUntimedQuestions && timeoutSeconds == 0 && timeoutBehavior == "cancel"))
                || presentation == "overlay" && timeoutSeconds is > 120)
                return reply.Reply(new AskUserResult([], "invalid", "Fix the presentation, timeout or timeout behavior."), true);
            var asked = questions.Select(Question).ToArray();
            var submitDefaults = timeoutBehavior == "submit_defaults";
            if (submitDefaults && asked.Any(question => !HasDefault(question)
                || question.Input is null && question.PathKind is null && question.PickerKind is null
                   && !question.MultiSelect && question.Options.Count(option => option.Recommended) > 1))
                return reply.Reply(new AskUserResult([], "invalid",
                    "Automatic answers require every question to have its recommended option or a 'default'."), true);
            if (answerStyle is not ("form" or "buttons"))
                return reply.Reply(new AskUserResult([], "invalid", "Fix the call and ask again.", "'answerStyle' must be 'form' or 'buttons'."), true);
            var replyButtons = answerStyle == "buttons";
            if (replyButtons && ValidateReplyButtons(questions, submitDefaults) is { } unfit)
                return reply.Reply(new AskUserResult([], "invalid", "Fix the call and ask again, or use answerStyle='form'.", unfit), true);

            // A background run has nobody watching it, so it is told so at once rather than made to wait
            // out a timeout no one will interrupt. The caller that started it is the one with a user.
            if (!run.Interactive && presentation != "overlay")
                return reply.Reply(new AskUserResult([], "dismissed",
                    "You are running as a background subtask; there is no user to answer you and there will not be one. "
                    + "Do what you can without this decision, and name the unresolved choice in your final answer so the "
                    + "conversation that started you can put it to the user."));

            var response = await broker().AskAsync(run,
                new UserPromptRequest(asked, presentation, submitDefaults, replyButtons),
                timeoutSeconds == 0 ? Timeout.InfiniteTimeSpan
                    : timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds)
                    : presentation == "overlay" || submitDefaults ? TimeSpan.FromSeconds(30) : Patience, cancellationToken);

            var answers = response.Answers
                .Select(answer => Reply(asked, answer))
                .OfType<AskUserReply>()
                .Where(Answered)
                .GroupBy(reply => reply.Id, StringComparer.Ordinal).Select(group => group.First())
                .ToArray();

            if (presentation == "overlay" && response.Outcome is not UserPromptOutcome.Answered)
                return reply.Reply(new AskUserResult(answers, Outcome(response.Outcome),
                    "The outside-chat question was cancelled or unanswered. Stop the dependent action and this guide; do not choose defaults or ask again."));
            if (response.Outcome is UserPromptOutcome.Declined or UserPromptOutcome.Interrupted)
                return reply.Reply(new AskUserResult(answers, Outcome(response.Outcome), Guidance(response.Outcome, answers, [], [])));

            // What the person left open is decided by the defaults the model gave, and that decision
            // goes into the answer, so the person can see what the assistant went on with.
            var open = asked.Where(question => answers.All(answer => answer.Id != question.Id)).ToArray();
            var assumed = open.Select(Assume).OfType<AskUserReply>().ToArray();
            var undecided = open.Where(question => assumed.All(answer => answer.Id != question.Id)).Select(question => question.Id).ToArray();
            var all = asked.Select(question => answers.Concat(assumed).FirstOrDefault(answer => answer.Id == question.Id))
                .OfType<AskUserReply>().ToArray();
            return reply.Reply(new AskUserResult(all, Outcome(response.Outcome), Guidance(response.Outcome, answers, assumed, undecided)));
        }

        private static bool Answered(AskUserReply reply) =>
            reply.Selected.Length > 0 || !string.IsNullOrWhiteSpace(reply.Other) || reply.Paths is { Count: > 0 } || reply.Values is { Count: > 0 };

        private static bool HasDefault(UserPromptQuestion question) =>
            question.Options.Any(option => option.Recommended) || question.Defaults is { Count: > 0 }
            || question.Input?.Kind == UserPromptInputKind.Rank;

        private static string Outcome(UserPromptOutcome outcome) => outcome switch
        {
            UserPromptOutcome.Answered => "answered",
            UserPromptOutcome.Dismissed => "dismissed",
            UserPromptOutcome.Expired => "expired",
            UserPromptOutcome.Declined => "declined",
            _ => "interrupted"
        };

        private static string Guidance(UserPromptOutcome outcome, AskUserReply[] answers, AskUserReply[] assumed, string[] undecided)
        {
            // Unlike dismissing, declining hands nothing over: choosing for them anyway is exactly
            // what they just refused, so the only thing left to do is stop and listen.
            if (outcome == UserPromptOutcome.Declined)
                return "The user declined to answer and does not want you to choose for them. Do not proceed with the work "
                       + "this question was about and do not ask it again. End your turn with a short reply that says what "
                       + "you stopped and wait for the user's next message.";
            if (outcome == UserPromptOutcome.Interrupted) return "There is no answer. " + DecideYourself;

            var parts = new List<string>
            {
                outcome switch
                {
                    UserPromptOutcome.Answered when assumed.Length == 0 && undecided.Length == 0 => "Proceed on these answers.",
                    // Partial answers are the common case, not an edge one: a person answers what they
                    // care about. Naming what came back empty is what stops the model asking again.
                    UserPromptOutcome.Answered => "Proceed on the answers given.",
                    UserPromptOutcome.Expired => "Nobody answered in time.",
                    _ => "The user declined to decide and left it to you."
                }
            };
            if (assumed.Length > 0)
                parts.Add("Your defaults were taken for " + string.Join(", ", assumed.Select(answer => answer.Id))
                          + " (assumed=true) and the user sees them as your choice; proceed on them, and if the work shows a "
                          + "different choice is better, say so in your reply.");
            if (undecided.Length > 0)
                parts.Add("These were left to you with no default: " + string.Join(", ", undecided) + ". " + DecideYourself);
            else if (assumed.Length > 0)
                parts.Add("Do not ask the same question again.");
            var remembered = answers.Where(answer => answer.Remember == true).Select(answer => answer.Id).ToArray();
            if (remembered.Length > 0)
                parts.Add("The user asked you to remember their answer to " + string.Join(", ", remembered)
                          + ": save each as a lasting preference with app_memory (follow the memory-save playbook: search first, "
                          + "update rather than duplicate) so this is not asked again.");
            return string.Join(" ", parts);
        }

        private static UserPromptQuestion Question(AskUserQuestion question)
        {
            var input = question.Input is { } value
                ? new UserPromptInput((value.Kind ?? string.Empty).Trim().ToLowerInvariant(), value.Min, value.Max, value.Step,
                    string.IsNullOrWhiteSpace(value.Unit) ? null : value.Unit.Trim(),
                    value.Marks?.Select(mark => new UserPromptMark(mark.Value, mark.Label?.Trim() ?? string.Empty)).ToArray(),
                    value.Multiline, string.IsNullOrWhiteSpace(value.Language) ? null : value.Language.Trim(),
                    string.IsNullOrEmpty(value.Pattern) ? null : value.Pattern,
                    string.IsNullOrWhiteSpace(value.Placeholder) ? null : value.Placeholder.Trim(), value.MaxLength)
                : null;
            // A slider, a scale, an order or a colour is the whole answer; typing something else
            // beside it would be a second answer. Text is nothing but the typed answer.
            var allowOther = input?.Kind switch
            {
                UserPromptInputKind.Text => true,
                UserPromptInputKind.Search or null => question.AllowOther,
                _ => false
            };
            return new UserPromptQuestion(
                question.Id,
                question.Text,
                question.Label,
                (question.Options ?? []).Select(option => new UserPromptOption(option.Label, option.Description, option.Recommended,
                    string.IsNullOrWhiteSpace(option.Value) ? null : option.Value.Trim())).ToArray(),
                question.MultiSelect,
                allowOther,
                question.PathKind?.Trim().ToLowerInvariant(),
                question.PickerKind?.Trim().ToLowerInvariant(),
                question.RepositoryPath?.Trim(),
                question.Revision,
                input,
                Defaults(question.Default),
                string.IsNullOrWhiteSpace(question.Preview) ? null : question.Preview.Trim(),
                question.Rememberable);
        }

        /// <summary>A default as the model wrote it — a number, a string or a list of either — in the strings the card holds.</summary>
        private static string[]? Defaults(JsonElement? value)
        {
            static string? One(JsonElement item) => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => item.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                JsonValueKind.Object => item.GetRawText(),
                _ => null
            };
            if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            var items = element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Select(One).ToArray()
                : [One(element)];
            var values = items.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray();
            return values.Length == 0 ? null : values;
        }

        /// <summary>
        /// The answer a question's default gives: its recommended options, its rank as offered, or its
        /// 'default'. Null when the model gave none, which leaves the choice open.
        /// </summary>
        private AskUserReply? Assume(UserPromptQuestion question)
        {
            var kind = question.Input?.Kind;
            var selected = kind == UserPromptInputKind.Rank
                ? Enumerable.Range(0, question.Options.Count).ToArray()
                : question.Options.Select((option, index) => (option, index)).Where(item => item.option.Recommended)
                    .Select(item => item.index).Take(question.MultiSelect ? int.MaxValue : 1).ToArray();
            var defaults = question.Defaults ?? [];
            // Where the default travels is where the card would have put it: typed text and a single
            // path in 'other', several directories in 'paths', every other value in 'values'.
            var typed = kind == UserPromptInputKind.Text || question.PathKind is DirectoryPath or FilePath
                || kind is null && question.PickerKind is null && question.PathKind is null && question.Options.Count == 0;
            var answer = new UserPromptAnswer(question.Id, selected,
                typed && defaults.Count > 0 ? defaults[0] : null,
                question.PathKind == DirectoriesPath ? defaults : null,
                typed || question.PathKind == DirectoriesPath ? null : defaults);
            var assumed = Reply([question], answer);
            return assumed is null || !Answered(assumed) ? null : assumed with { Assumed = true, Remember = null };
        }

        /// <summary>
        /// Turns positions back into labels. An answer naming an option that no longer exists is
        /// dropped rather than guessed at: the two sides of this conversion are a process apart, and a
        /// mismatch means the question was replaced, not that the person meant the neighbouring choice.
        /// </summary>
        private AskUserReply? Reply(IReadOnlyList<UserPromptQuestion> questions, UserPromptAnswer answer)
        {
            var question = questions.FirstOrDefault(item => string.Equals(item.Id, answer.QuestionId, StringComparison.Ordinal));
            if (question is null) return null;
            var kind = question.Input?.Kind;
            var positions = answer.Selected.Where(index => index >= 0 && index < question.Options.Count).ToArray();
            var indices = kind switch
            {
                // A rank is every option once, in the order given; anything it leaves out keeps its
                // place after the ones that were moved.
                UserPromptInputKind.Rank => positions.Length == 0 ? []
                    : positions.Concat(Enumerable.Range(0, question.Options.Count)).Distinct().ToArray(),
                // A slider's or a swatch's option is a preset of the value, not a choice of its own.
                UserPromptInputKind.Number or UserPromptInputKind.Range or UserPromptInputKind.Scale or UserPromptInputKind.Color => [],
                _ => positions
            };
            var selected = indices.Select(index => question.Options[index].Label).ToArray();
            var paths = question.PathKind == DirectoriesPath
                ? answer.Paths?.Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : null;
            var picker = question.PickerKind;
            var other = string.IsNullOrWhiteSpace(answer.Other) ? null
                : kind == UserPromptInputKind.Text ? inputs.Normalize(question, answer.Other)
                : question.AllowOther || question.PathKind is not null ? answer.Other.Trim() : null;

            string[] values;
            if (kind is UserPromptInputKind.Number or UserPromptInputKind.Scale or UserPromptInputKind.Color)
                values = (answer.Values ?? []).Select(value => inputs.Normalize(question, value)).OfType<string>().Take(1).ToArray();
            else if (kind == UserPromptInputKind.Range)
            {
                var ends = (answer.Values ?? []).Select(value => inputs.Normalize(question, value)).OfType<string>().Take(2).ToArray();
                values = ends.Length < 2 ? [] : ends.OrderBy(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            }
            else
            {
                var picked = picker is "branch" or "commit"
                    ? answer.Values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                    : SchedulePickers.IsSchedulePicker(picker)
                        ? answer.Values?.Select(value => SchedulePickers.Normalize(picker, value, calendar)).OfType<string>()
                        : null;
                // A chosen option stands for its value, in the order it was chosen; picked values follow.
                var chosen = indices
                    .Select(index => question.Options[index].Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => SchedulePickers.IsSchedulePicker(picker) ? SchedulePickers.Normalize(picker, value, calendar) : value!.Trim())
                    .OfType<string>();
                values = chosen.Concat(picked ?? []).Distinct(StringComparer.Ordinal)
                    .Take(question.MultiSelect || kind == UserPromptInputKind.Rank ? 200 : 1).ToArray();
            }

            var described = picker == SchedulePickers.Recurrence
                ? values.Select(value => ScheduleCalendar.TryDeserialize(value, out var recurrence)
                    ? descriptions.Describe(recurrence!) : value).ToArray()
                : inputs.Describe(question, values) is { } words ? [words] : null;
            double? number = kind is UserPromptInputKind.Number or UserPromptInputKind.Scale && values.Length == 1
                ? double.Parse(values[0], CultureInfo.InvariantCulture) : null;
            var range = kind == UserPromptInputKind.Range && values.Length == 2
                ? new AskUserRange(double.Parse(values[0], CultureInfo.InvariantCulture), double.Parse(values[1], CultureInfo.InvariantCulture))
                : null;
            return new AskUserReply(question.Id, selected,
                other,
                paths is { Length: > 0 } ? paths : null,
                values is { Length: > 0 } ? values : null,
                described is { Length: > 0 } ? described : null,
                number,
                range,
                kind == UserPromptInputKind.Rank && selected.Length > 0 ? true : null,
                null,
                answer.Remember && question.Rememberable ? true : null);
        }

        /// <summary>
        /// Says what is wrong in terms the model can act on, because it is the one that will fix it.
        /// Every limit here is about the card staying readable: a question nobody can take in at a
        /// glance is worse than no question at all.
        /// </summary>
        private string? Validate(AskUserQuestion[]? questions)
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
                    if (picker is not ("branch" or "commit") && !SchedulePickers.IsSchedulePicker(picker))
                        return $"The 'pickerKind' of '{question.Id}' must be 'branch', 'commit', 'date', 'time' or 'recurrence'.";
                    if (path is { Length: > 0 }) return $"Question '{question.Id}' cannot combine 'pathKind' and 'pickerKind'.";
                    if (picker is "branch" or "commit"
                        && (string.IsNullOrWhiteSpace(question.RepositoryPath) || !Path.IsPathFullyQualified(question.RepositoryPath)))
                        return $"Question '{question.Id}' needs an absolute 'repositoryPath' for its Git picker.";
                }
                if (question.Input is { } input && string.IsNullOrWhiteSpace(input.Kind))
                    return $"The 'input' of '{question.Id}' needs a 'kind'.";
                foreach (var option in options)
                {
                    if (string.IsNullOrWhiteSpace(option.Label)) return $"Question '{question.Id}' has an option with no label.";
                    if (option.Label.Length > MaxOptionLength) return $"An option of '{question.Id}' is longer than {MaxOptionLength} characters.";
                    if (option.Description is { Length: > MaxDescriptionLength })
                        return $"An option description of '{question.Id}' is longer than {MaxDescriptionLength} characters.";
                    if (option.Value is { Length: > MaxValueLength })
                        return $"An option value of '{question.Id}' is longer than {MaxValueLength} characters.";
                    if (SchedulePickers.IsSchedulePicker(picker) && !string.IsNullOrWhiteSpace(option.Value)
                        && SchedulePickers.Normalize(picker, option.Value, calendar) is null)
                        return $"The value of option '{option.Label}' in '{question.Id}' is not a valid {picker}: use "
                            + (picker == SchedulePickers.Date ? "yyyy-MM-dd" : picker == SchedulePickers.Time ? "HH:mm"
                                : "recurrence JSON such as {\"frequency\":\"Weekly\",\"start\":\"2026-10-12\",\"time\":\"09:00\",\"weekdays\":[\"Monday\"]}") + ".";
                }
                var asked = Question(question);
                // The input is checked first: an input the card cannot draw explains every other
                // complaint, such as having no options to answer with.
                if (inputs.Validate(asked) is { } invalid) return invalid;
                var maxOptions = inputs.MaxOptions(asked);
                if (options.Length > maxOptions)
                    return $"Question '{question.Id}' offers more than {maxOptions} options"
                           + (asked.Input is null ? "; use input kind 'search' for a long list." : ".");
                // A path question is answerable through its picker and a slider or swatch through its
                // control, so they need neither options nor the free-text box the others fall back on.
                if (options.Length == 0 && !asked.AllowOther && path is not { Length: > 0 } && picker is not { Length: > 0 }
                    && asked.Input?.Kind is not (UserPromptInputKind.Number or UserPromptInputKind.Range
                        or UserPromptInputKind.Scale or UserPromptInputKind.Color))
                    return $"Question '{question.Id}' offers no options and no free-text answer, so it cannot be answered.";
            }

            return null;
        }

        /// <summary>
        /// A click that sends at once only works for a question one click can answer: a single
        /// question with one choice, and few enough short choices to be read as a row of buttons.
        /// </summary>
        private static string? ValidateReplyButtons(AskUserQuestion[] questions, bool submitDefaults)
        {
            if (questions.Length != 1) return "answerStyle='buttons' asks exactly one question.";
            var question = questions[0];
            if (question.MultiSelect) return "answerStyle='buttons' cannot use 'multiSelect': one click is the whole answer.";
            if (!string.IsNullOrWhiteSpace(question.PathKind) || !string.IsNullOrWhiteSpace(question.PickerKind) || question.Input is not null)
                return "answerStyle='buttons' cannot use 'pathKind', 'pickerKind' or 'input'.";
            var options = question.Options ?? [];
            if (options.Length is < MinReplyButtons or > MaxReplyButtons)
                return $"answerStyle='buttons' needs {MinReplyButtons}-{MaxReplyButtons} options.";
            if (submitDefaults) return "answerStyle='buttons' cannot use timeoutBehavior='submit_defaults'.";
            return null;
        }
    }
}
