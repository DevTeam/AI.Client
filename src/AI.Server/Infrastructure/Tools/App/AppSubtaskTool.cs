namespace AI.Mcp.App;

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

/// <summary>One piece of work to delegate, and optionally the connection that should answer it.</summary>
/// <param name="Task">What the subtask is asked to do.</param>
/// <param name="ConnectionId">
/// Which connection answers this one. Omitted means the call's own 'connectionId', and failing that
/// the calling chat's. Naming one per task is what lets several models be exercised at once: tasks
/// of a single call run together, while separate calls do not.
/// </param>
public sealed record SubtaskRequest(string Task, Guid? ConnectionId = null);

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
    Func<IToolAutoApprover> autoApprover) : IAppTool
{
    /// <summary>
    /// How many subtask runs may be in flight across the Host at once. A nested subtask holds its
    /// parent open, so this bounds depth and width together: runaway recursion stops here instead
    /// of consuming the machine.
    /// </summary>
    private const int MaxConcurrentRuns = 8;

    /// <summary>
    /// Matches the concurrency ceiling: one fan-out may use every slot, because checking a set of
    /// connections in one call is the case this exists for, and nesting is what the ceiling guards.
    /// </summary>
    private const int MaxTasks = 8;

    private const int MaxEntryLength = 2000;

    private static int _running;

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
                Description = "Delegate work to a separate conversation and get back only its answer, so the details never enter your own "
                              + "context. It runs in your project and chat unless 'projectId' and 'chatId' name others, and inherits their "
                              + "directory grants and tool policies. Each task answers through its own 'connectionId' if it names an enabled one, else the call's "
                              + "'connectionId', else one of the connections marked for subtasks — several may be, and tasks naming none "
                              + "are dealt out over them in turn — else the calling chat's. Read the settings resource to "
                              + "see what each connection is worth: a connection may carry a capability and a cost from 1 to 5 and a line on "
                              + "what it is good for, so mechanical work can go to a cheaper model and hard work to a stronger one. Tasks of "
                              + "one call run at the same time, while separate calls do not, so put every task you want run in parallel into "
                              + "a single call. A subtask has nobody "
                              + "to ask for confirmation, so any tool whose policy is 'Ask' is refused to it — allow such tools beforehand if "
                              + "a subtask needs them. Nothing is saved: there is no chat to open afterwards, though the full exchange is "
                              + "attached to this result for the user to read."
            });

        [McpServerTool(Name = "spawn_subtask", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true,
            UseStructuredContent = true, OutputSchemaType = typeof(SubtaskResult))]
        private Task<CallToolResult> RunAsync(
            SubtaskRequest[] tasks,
            IProgress<ProgressNotificationValue> progress,
            Guid? projectId = null,
            Guid? chatId = null,
            Guid? connectionId = null,
            CancellationToken cancellationToken = default) =>
            // Omitted ids mean the run's own project and chat, which is almost always what is meant
            // and is the one pair a model cannot get wrong.
            tool.RunAsync(run, projectId ?? run.ProjectId, chatId ?? run.ChatId, tasks, progress, connectionId, cancellationToken);
    }

    private async Task<CallToolResult> RunAsync(
        ToolRunContext run,
        Guid projectId,
        Guid chatId,
        SubtaskRequest[] tasks,
        IProgress<ProgressNotificationValue> progress,
        Guid? connectionId = null,
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
                RequestTemplateAsync(projectId, chatId, item.Chosen, item.Turn, cancellationToken)));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return Failed(error.Message);
        }

        var claimed = Interlocked.Add(ref _running, tasks.Length);
        if (claimed > MaxConcurrentRuns)
        {
            Interlocked.Add(ref _running, -tasks.Length);
            return Failed($"Too many subtasks are already running ({MaxConcurrentRuns} at most, including nested ones).");
        }

        try
        {
            var board = new Board(progress, tasks.Length);
            var runs = tasks.Select((item, index) => RunOneAsync(run, projectId, chatId,
                endpoints[index].Template, endpoints[index].Name, item.Task, index, board, cancellationToken)).ToArray();
            var completed = await Task.WhenAll(runs);
            return reply.Reply(
                new SubtaskResult(completed.Select(item => item.Outcome).ToArray(), null),
                new SubtaskTranscript(completed.SelectMany(item => item.Transcript).ToArray()),
                completed.Any(item => item.Outcome.Error is not null));
        }
        finally
        {
            Interlocked.Add(ref _running, -tasks.Length);
        }
    }

    private async Task<(SubtaskOutcome Outcome, IReadOnlyList<SubtaskTranscriptEntry> Transcript)> RunOneAsync(
        ToolRunContext run, Guid projectId, Guid chatId, ChatCompletionRequest template, string connection, string task,
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
        try
        {
            var changes = await agent().RunAsync(projectId, chatId,
                // A branch of its own keeps the subtask's workspace baseline apart from the parent's,
                // so repeated edits here collapse against what this subtask found rather than against
                // what the caller found. The caller is still told the total: the run is registered as
                // its child below, and a parent's snapshot covers its children.
                Guid.CreateVersion7(),
                template with { Message = task, ContextMessages = [new ChatCompletionMessage("user", task)] },
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
                parentBranchId: run.BranchId);
            var text = answer.ToString();
            transcript.Add(new SubtaskTranscriptEntry(task, "assistant", text, null));
            board.Finish(index);
            return (new SubtaskOutcome(task, text, connection, transcript.Count, toolCalls, changes.Files.Count, true, null,
                elapsed.ElapsedMilliseconds), transcript);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One failed subtask does not fail its siblings: the caller is told which one broke and
            // keeps whatever the others produced.
            board.Finish(index);
            return (new SubtaskOutcome(task, answer.ToString(), connection, transcript.Count, toolCalls, 0, false, error.Message,
                elapsed.ElapsedMilliseconds), transcript);
        }
    }

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
        Guid projectId, Guid chatId, Guid? requested, int turn, CancellationToken cancellationToken)
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
        var inheritedConnectionId = chat.ConnectionId ?? project.ConnectionId;
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
            await secrets.GetAsync("connection", connection.Id, cancellationToken), string.Empty, connection.Id, []),
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
