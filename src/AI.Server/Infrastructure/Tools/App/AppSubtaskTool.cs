namespace AI.Mcp.App;

using AI.Application.Usage;
using AI.Contracts.Usage;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Runs;
using AI.Contracts.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

/// <summary>One piece of work to delegate, and optionally the connection that should answer it.</summary>
/// <param name="Task">What the subtask is asked to do.</param>
/// <param name="ConnectionId">
/// Which connection answers this one. Omitted means the call's own 'connectionId', and failing that
/// the calling chat's. Naming one per task is what lets several models be exercised at once: tasks
/// of a single call run together, while separate calls do not.
/// </param>
public sealed record SubtaskRequest(
    string Task,
    Guid? ConnectionId = null,
    [property: Description("Concrete conditions this task must verify before it reports completion.")]
    [property: MaxLength(32)] string[]? AcceptanceCriteria = null);

/// <summary>A file changed by the subtask, measured from that subtask's own workspace baseline.</summary>
public sealed record SubtaskArtifact(
    string Path,
    string Change,
    int? Additions = null,
    int? Deletions = null,
    string? PreviousPath = null);

/// <summary>A check the subtask actually performed, not a check the parent still has to repeat.</summary>
/// <param name="Status">Passed, Failed or NotRun.</param>
public sealed record SubtaskCheck(string Name, string Status, string? Details = null);

/// <summary>What one delegated task produced. The transcript is deliberately not here.</summary>
/// <param name="Answer">The subtask's final reply, which is all the calling model is given.</param>
/// <param name="Connection">Which connection answered, so the record shows what the work was worth.</param>
/// <param name="ElapsedMilliseconds">Wall time of the delegated run, including its model and tool calls; not provider-only latency.</param>
public sealed record SubtaskOutcome(
    string Task,
    string Answer,
    string Connection,
    int Messages,
    int ToolCalls,
    int FilesChanged,
    bool Completed,
    string? Error,
    IReadOnlyList<SubtaskArtifact> Artifacts,
    IReadOnlyList<SubtaskCheck> Checks,
    IReadOnlyList<string> Unresolved,
    long ElapsedMilliseconds = 0);

public sealed record SubtaskResult(IReadOnlyList<SubtaskOutcome> Results, string? Error);

/// <summary>One turn of a subtask, kept for the user to read and withheld from the model.</summary>
public sealed record SubtaskTranscriptEntry(string Task, string Role, string Content, string? ToolName);

public sealed record SubtaskTranscript(IReadOnlyList<SubtaskTranscriptEntry> Transcript);

/// <summary>
/// Runs a task in a conversation of its own and hands back only its conclusion.
/// </summary>
/// <remarks>
/// The point is the split between audiences. The subtask's whole exchange — its reasoning, every
/// tool call and every tool result — goes into <c>_meta</c>, which the contract keeps out of the
/// model-facing projection, so the user can open and read it while the caller pays context for the
/// answer alone. Delegating work this way is cheaper than reading a second chat back, which would
/// import exactly the text the delegation was meant to avoid.
///
/// Nothing is stored: the subtask's messages live in a list for the duration of the call. There is
/// no chat to find afterwards, which is the trade for not having one to clean up.
/// </remarks>
[McpServerToolType]
public sealed class AppSubtaskTool(
    Func<IChatAgent> agent,
    IProjectService projects,
    IChatService chats,
    IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets,
    IToolPresentations presentations,
    IToolResultCodec toolResultCodec,
    IAppToolReply reply,
    Func<IToolAutoApprover> autoApprover,
    ITokenUsageMeter usageMeter, IChatBranchSettingsResolver branchSettings) : IAppTool
{
    private const string EvidenceStart = "<subtask_evidence>";
    private const string EvidenceEnd = "</subtask_evidence>";

    /// <summary>
    /// How many subtask runs may be in flight across the Host at once. Extra work in a root fan-out
    /// waits for a slot, while nested work never waits behind its own parents and fails instead;
    /// this bounds width without creating a recursive deadlock.
    /// </summary>
    private const int MaxConcurrentRuns = 32;

    /// <summary>
    /// One fan-out may use every execution slot. If other calls already occupy slots, queued work
    /// starts as those calls finish instead of making the model submit the remaining tasks again.
    /// </summary>
    private const int MaxTasks = 256;

    private const int MaxEntryLength = 2000;

    private static readonly SemaphoreSlim Slots = new(MaxConcurrentRuns, MaxConcurrentRuns);

    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();
    private CallToolResult Failed(string error) => reply.Reply(new SubtaskResult([], error), true);

    /// <summary>
    /// Binds the tool to the run that opened the session. The tool itself is shared by every
    /// session, so the calling run cannot be held on it; the call needs it because a subtask is
    /// tracked as a child of whoever delegated it, and what identifies that parent is its branch.
    /// </summary>
    private sealed class Session(AppSubtaskTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(
            RunAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Delegate independent self-contained work to separate conversations and return compact answers, artifacts, checks and unresolved items. "
                              + "Put all parallel tasks in one call, give each explicit acceptance criteria, use disjoint write targets, and put common rules in "
                              + $"'sharedInstructions'. At most {MaxConcurrentRuns} tasks run at once; excess tasks wait in this call. Omitted project/chat ids use the current ones. "
                              + "A task's connectionId wins, then the call's, then the marked subtask pool, then the current chat model. Subtasks inherit directory grants "
                              + "and policies but cannot ask for approval: obtain the narrowest saved permission before delegating when no allowed alternative exists. "
                              + "Nothing is saved as a chat; the full exchange is attached for the user but excluded from model context."
            });

        [McpServerTool(Name = "spawn_subtask", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true,
            UseStructuredContent = true, OutputSchemaType = typeof(SubtaskResult))]
        private Task<CallToolResult> RunAsync(
            [MinLength(1)] [MaxLength(MaxTasks)] SubtaskRequest[] tasks,
            IProgress<ProgressNotificationValue> progress,
            Guid? projectId = null,
            Guid? chatId = null,
            Guid? connectionId = null,
            [Description("Instructions common to every task, supplied once to keep the call compact.")]
            [MaxLength(20000)] string? sharedInstructions = null,
            CancellationToken cancellationToken = default) =>
            // Omitted ids mean the run's own project and chat, which is almost always what is meant
            // and is the one pair a model cannot get wrong.
            tool.RunAsync(run, projectId ?? run.ProjectId, chatId ?? run.ChatId, tasks, progress, connectionId,
                sharedInstructions, cancellationToken);
    }

    private async Task<CallToolResult> RunAsync(
        ToolRunContext run,
        Guid projectId,
        Guid chatId,
        SubtaskRequest[] tasks,
        IProgress<ProgressNotificationValue> progress,
        Guid? connectionId = null,
        string? sharedInstructions = null,
        CancellationToken cancellationToken = default)
    {
        if (tasks is not { Length: > 0 }) return Failed("At least one task is required.");
        if (tasks.Length > MaxTasks) return Failed($"At most {MaxTasks} tasks can be delegated in one call.");
        if (tasks.Any(item => item is null || string.IsNullOrWhiteSpace(item.Task))) return Failed("A task cannot be empty.");

        // Every connection is resolved before anything starts: a batch that would fail on its last
        // task's endpoint should not first spend a minute on the others.
        (ChatCompletionRequest Template, string Name)[] endpoints;
        try
        {
            // Only the tasks that named nothing take a turn in the rotation, so one pinned task
            // does not shift where the rest of the batch lands.
            var unaddressed = 0;
            var wanted = tasks.Select(item => item.ConnectionId ?? connectionId)
                .Select(chosen => (Chosen: chosen, Turn: chosen is null ? unaddressed++ : 0)).ToArray();
            endpoints = await Task.WhenAll(wanted.Select(item =>
                RequestTemplateAsync(projectId, chatId, chatId == run.ChatId ? run.BranchId : chatId,
                    item.Chosen, item.Turn, cancellationToken)));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return Failed(error.Message);
        }

        var board = new Board(progress, tasks.Length);
        var runs = tasks.Select((item, index) => RunQueuedAsync(run, projectId, chatId,
            endpoints[index].Template, endpoints[index].Name, item.Task,
            Prompt(sharedInstructions, item.Task, item.AcceptanceCriteria),
            index, board, cancellationToken)).ToArray();
        var completed = await Task.WhenAll(runs);
        return reply.Reply(
            new SubtaskResult(completed.Select(item => item.Outcome).ToArray(), null),
            new SubtaskTranscript(completed.SelectMany(item => item.Transcript).ToArray()),
            completed.Any(item => item.Outcome.Error is not null));
    }

    private async Task<(SubtaskOutcome Outcome, IReadOnlyList<SubtaskTranscriptEntry> Transcript)> RunQueuedAsync(
        ToolRunContext run, Guid projectId, Guid chatId, ChatCompletionRequest template, string connection, string task, string prompt,
        int index, Board board, CancellationToken cancellationToken)
    {
        board.Set(index, "queued");
        var entered = run.IsSubtask
            ? await Slots.WaitAsync(0, cancellationToken)
            : await WaitAsync(cancellationToken);
        if (!entered)
            return (new SubtaskOutcome(task, string.Empty, connection, 0, 0, 0, false,
                $"No subtask slot is available for nested work ({MaxConcurrentRuns} at most).", [], [],
                [$"The task did not start because all {MaxConcurrentRuns} subtask slots were occupied."]), []);
        try
        {
            return await RunOneAsync(run, projectId, chatId, template, connection, task, prompt, index, board, cancellationToken);
        }
        finally
        {
            Slots.Release();
        }
    }

    private static async Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        await Slots.WaitAsync(cancellationToken);
        return true;
    }

    private async Task<(SubtaskOutcome Outcome, IReadOnlyList<SubtaskTranscriptEntry> Transcript)> RunOneAsync(
        ToolRunContext run, Guid projectId, Guid chatId, ChatCompletionRequest template, string connection, string task, string prompt,
        int index, Board board, CancellationToken cancellationToken)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var transcript = new List<SubtaskTranscriptEntry>();
        var answer = new System.Text.StringBuilder();
        // A tool message names only the call it answers, so what that call was has to be remembered
        // from the message before it — without it a result cannot be described, only dumped.
        var made = new Dictionary<string, (string Name, string Arguments)>(StringComparer.Ordinal);
        var toolCalls = 0;
        board.Set(index, "starting");
        // Counted in the turn that delegated the work, which the scope inherits, but apart from
        // its own answer: a fan-out is where a turn's cost usually hides.
        using var usageScope = usageMeter.Begin(new TokenUsageScope(TokenUsagePurpose.Subtask));
        try
        {
            var changes = await agent().RunAsync(projectId, chatId,
                // A branch of its own keeps the subtask's workspace baseline apart from the parent's,
                // so repeated edits here collapse against what this subtask found rather than against
                // what the caller found. The caller is still told the total: the run is registered as
                // its child below, and a parent's snapshot covers its children.
                Guid.CreateVersion7(),
                template with { Message = prompt, ContextMessages = [new ChatCompletionMessage("user", prompt)] },
                (message, _) =>
                {
                    if (message.ToolCalls is { Count: > 0 } calls)
                    {
                        toolCalls += calls.Count;
                        foreach (var call in calls) made[call.Id] = (call.Name, call.Arguments);
                        // This turn ended in tool calls, so whatever it streamed was thinking aloud
                        // on the way to the answer, not the answer. Only the last turn's text is.
                        answer.Clear();
                    }

                    transcript.Add(Entry(task, message, made, presentations));
                    return Task.CompletedTask;
                },
                (content, _) =>
                {
                    if (answer.Length == 0 && content.Length > 0) board.Set(index, "writing");
                    answer.Append(content);
                    return Task.CompletedTask;
                },
                // A subtask that reports nothing looks identical to one that has hung, so what it is
                // doing right now is forwarded to the caller's own progress channel and lands in the
                // live row of the tool card that started it.
                (activity, _) =>
                {
                    board.Set(index, activity is { } running ? ToolRef.Parse(running.Name).Name : "thinking");
                    return Task.CompletedTask;
                },
                (_, _) => Task.CompletedTask,
                // Nobody is watching a background run, so anything that would stop to ask is refused —
                // unless the chat's own mode would have answered it without asking anyway. The
                // subtask is told when it is refused and can report what it could not do.
                async (tool, arguments, _, _, ct) =>
                    (await autoApprover().DecideAsync(projectId, chatId, run.BranchId, tool, arguments, ct)).Allowed
                        ? ToolApprovalAction.Allow
                        : ToolApprovalAction.Deny,
                cancellationToken,
                // Same reason, said once for every tool rather than per callback: this run has no
                // person behind it, so ask_user answers itself instead of waiting for one.
                interactive: false,
                parentBranchId: run.BranchId,
                maxModelSteps: 40);
            var rawText = answer.ToString();
            var (text, evidence) = ExtractEvidence(rawText);
            transcript.Add(new SubtaskTranscriptEntry(task, "assistant", rawText, null));
            board.Finish(index);
            var artifacts = changes.Files.Select(file => new SubtaskArtifact(file.Path, file.Kind.ToString(),
                file.Additions, file.Deletions, file.PreviousPath)).ToArray();
            return (new SubtaskOutcome(task, text, connection, transcript.Count, toolCalls, changes.Files.Count, true, null,
                artifacts, evidence.Checks, evidence.Unresolved, elapsed.ElapsedMilliseconds), transcript);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One failed subtask does not fail its siblings: the caller is told which one broke and
            // keeps whatever the others produced.
            board.Finish(index);
            return (new SubtaskOutcome(task, answer.ToString(), connection, transcript.Count, toolCalls, 0, false, error.Message,
                [], [], [error.Message], elapsed.ElapsedMilliseconds), transcript);
        }
    }

    private static string Prompt(string? sharedInstructions, string task, IReadOnlyList<string>? acceptanceCriteria)
    {
        var prompt = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(sharedInstructions))
            prompt.Append(sharedInstructions.Trim()).AppendLine().AppendLine();
        prompt.Append(task.Trim());
        if (acceptanceCriteria is { Count: > 0 })
        {
            prompt.AppendLine().AppendLine().AppendLine("Acceptance criteria:");
            foreach (var criterion in acceptanceCriteria.Where(item => !string.IsNullOrWhiteSpace(item)))
                prompt.Append("- ").AppendLine(criterion.Trim());
        }
        prompt.AppendLine().AppendLine()
            .AppendLine("Verify your own result before finishing. Report only checks you actually ran; do not mark an inferred or skipped check as passed.")
            .Append("End the response with one machine-readable line in this exact form: ")
            .Append(EvidenceStart)
            .Append("{\"checks\":[{\"name\":\"check performed\",\"status\":\"Passed|Failed|NotRun\",\"details\":\"concise evidence\"}],\"unresolved\":[\"remaining issue\"]}")
            .Append(EvidenceEnd)
            .Append(". Use empty arrays when there are no checks or unresolved items.");
        return prompt.ToString();
    }

    private static (string Answer, SubtaskEvidence Evidence) ExtractEvidence(string text)
    {
        var start = text.LastIndexOf(EvidenceStart, StringComparison.Ordinal);
        if (start < 0)
            return (text, new SubtaskEvidence([], ["The subtask did not provide structured verification evidence."]));
        var jsonStart = start + EvidenceStart.Length;
        var end = text.IndexOf(EvidenceEnd, jsonStart, StringComparison.Ordinal);
        if (end < 0)
            return (text, new SubtaskEvidence([], ["The subtask returned malformed verification evidence."]));
        try
        {
            var parsed = JsonSerializer.Deserialize<ParsedSubtaskEvidence>(text[jsonStart..end], EvidenceJson);
            if (parsed is null) throw new JsonException("Empty evidence.");
            var checks = (parsed.Checks ?? []).Where(check => check is not null
                    && !string.IsNullOrWhiteSpace(check.Name)
                    && check.Status is "Passed" or "Failed" or "NotRun")
                .Select(check => check with { Name = check.Name.Trim(), Details = check.Details?.Trim() }).ToArray();
            var unresolved = (parsed.Unresolved ?? []).Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()).ToArray();
            return ((text[..start] + text[(end + EvidenceEnd.Length)..]).TrimEnd(), new SubtaskEvidence(checks, unresolved));
        }
        catch (JsonException)
        {
            return (text, new SubtaskEvidence([], ["The subtask returned malformed verification evidence."]));
        }
    }

    private static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerDefaults.Web);
    private sealed record ParsedSubtaskEvidence(IReadOnlyList<SubtaskCheck>? Checks, IReadOnlyList<string>? Unresolved);
    private sealed record SubtaskEvidence(IReadOnlyList<SubtaskCheck> Checks, IReadOnlyList<string> Unresolved);

    /// <summary>
    /// Turns one message of a subtask into a line a person can read. A tool result is stored as the
    /// protocol's own JSON, and dropping that into the transcript verbatim produces escaped JSON
    /// inside escaped JSON — technically complete and practically unreadable. It is described by the
    /// same adapters that describe every other tool result instead.
    /// </summary>
    private SubtaskTranscriptEntry Entry(
        string task, ChatCompletionMessage message, Dictionary<string, (string Name, string Arguments)> made,
        IToolPresentations presentations)
    {
        if (message.ToolCallId is not { Length: > 0 } callId || !made.TryGetValue(callId, out var call))
            return new SubtaskTranscriptEntry(task, message.Role, Clamp(message.Content),
                message.ToolCalls is { Count: > 0 } named ? ToolRef.Parse(named[0].Name).Name : null);
        var described = presentations.DescribeResult(call.Name, call.Arguments,
            toolResultCodec.Read(message.Content));
        var text = described.Body is { Length: > 0 } body ? $"{described.Summary}{Environment.NewLine}{body}" : described.Summary;
        return new SubtaskTranscriptEntry(task, message.Role, Clamp(text), ToolRef.Parse(call.Name).Name);
    }

    /// <summary>
    /// Keeps one entry from swallowing the record. A single tool result can run to megabytes, and
    /// the transcript is stored in the parent's history, where that cost is paid on every read.
    /// </summary>
    private static string Clamp(string? text) =>
        text is null ? string.Empty : text.Length <= MaxEntryLength ? text : text[..MaxEntryLength] + "…";

    /// <summary>
    /// The connection the subtask speaks through: the one it was given, else one of those marked
    /// for subtasks, else the calling chat's or its project's. A named connection must be one of
    /// the user's own and switched on — the subtask chooses among what is configured, it does not
    /// describe an endpoint of its own.
    /// </summary>
    /// <param name="turn">
    /// Which unaddressed task this is. Several connections may be marked for subtasks, and the
    /// tasks that named none are dealt out over them in turn, so a fan-out is answered by several
    /// providers at once rather than queueing behind one.
    /// </param>
    private async Task<(ChatCompletionRequest Template, string Name)> RequestTemplateAsync(
        Guid projectId, Guid chatId, Guid branchId, Guid? requested, int turn, CancellationToken cancellationToken)
    {
        var chat = await chats.GetAsync(projectId, chatId, cancellationToken)
            ?? throw new InvalidOperationException("Chat not found.");
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        var global = await settings.LoadAsync(cancellationToken);
        // Naming a connection is exact and fails loudly. Naming none falls back to the ones marked
        // for subtasks — the whole point of that mark is that delegated work need not cost what the
        // conversation costs — and only then to whatever the conversation itself runs on.
        var marked = global.Connections.Where(item => item.ForSubtasks && item.Enabled).ToArray();
        var inheritedConnectionId = branchSettings.ConnectionId(chat, branchId) ?? project.ConnectionId;
        var connection = requested is { } named
            ? global.Connections.SingleOrDefault(item => item.Id == named && item.Enabled)
              ?? throw new InvalidOperationException("No enabled connection has that id. Read the settings to see which exist.")
            : marked.Length > 0
                ? marked[turn % marked.Length]
                : inheritedConnectionId is { } inherited
                    ? global.Connections.SingleOrDefault(item => item.Id == inherited && item.Enabled)
                      ?? throw new InvalidOperationException("The calling chat has no enabled connection.")
                    : global.Connections.SingleOrDefault(item => item.IsDefault && item.Enabled)
                      ?? throw new InvalidOperationException("The calling chat has no enabled connection.");
        return (new ChatCompletionRequest(connection.BaseUrl, connection.Model,
            await secrets.GetAsync("connection", connection.Id, cancellationToken), string.Empty, connection.Id, [],
            ReasoningEffort: connection.ReasoningEffort),
            connection.Name);
    }



    /// <summary>
    /// What every subtask is doing right now, collapsed into one line for the caller's progress
    /// channel. Held in one place because several subtasks share a single channel: reporting them
    /// independently would just overwrite each other with whichever finished its sentence last.
    /// </summary>
    private sealed class Board(IProgress<ProgressNotificationValue>? sink, int total)
    {
        private readonly string?[] _state = new string?[total];
        private readonly Lock _gate = new();
        private int _finished;

        public void Set(int index, string what)
        {
            lock (_gate) _state[index] = what;
            Report();
        }

        public void Finish(int index)
        {
            lock (_gate)
            {
                _state[index] = null;
                _finished++;
            }

            Report();
        }

        private void Report()
        {
            if (sink is null) return;
            string message;
            int finished;
            lock (_gate)
            {
                finished = _finished;
                // One subtask needs no numbering; several do, or the line says nothing about which
                // of them is where.
                message = string.Join(" · ", _state
                    .Select((what, index) => what is null ? null : total == 1 ? what : $"{index + 1}: {what}")
                    .OfType<string>());
            }

            if (finished > 0 && total > 1)
                message = message.Length == 0 ? $"{finished}/{total} done" : $"{finished}/{total} done · {message}";
            sink.Report(new ProgressNotificationValue { Progress = finished, Total = total, Message = message });
        }
    }
}
