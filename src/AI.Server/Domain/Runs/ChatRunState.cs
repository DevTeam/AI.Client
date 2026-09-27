using AI.Domain.Common;
// ReSharper disable InvertIf

namespace AI.Domain.Runs;

public sealed class ChatRunState(Guid projectId, Guid chatId, Guid branchId)
{
    private readonly List<QueuedRunMessage> _queue = [];
    private readonly HashSet<Guid> _operations = [];

    public Guid ProjectId { get; } = projectId;

    public Guid ChatId { get; } = chatId;

    public Guid BranchId { get; } = branchId;

    public RunStatus Status { get; private set; }

    public string StreamingContent { get; private set; } = string.Empty;

    public string? Error { get; private set; }

    public RunFailureKind FailureKind { get; private set; }

    public bool CanRetry => FailureKind is RunFailureKind.None or RunFailureKind.Transient
        or RunFailureKind.Storage or RunFailureKind.ContextWindow;

    public bool HasUnreadResponse { get; private set; }

    public long Revision { get; private set; }

    public IReadOnlyList<QueuedRunMessage> Queue => _queue;

    public IReadOnlySet<Guid> Operations => _operations;

    public bool Enqueue(Guid operationId, QueuedRunMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Content) && message.Resources is not { Count: > 0 })
            throw new DomainException("Queued message cannot be empty.");
        if (!_operations.Add(operationId)) return false;
        _queue.Add(message); Revision++; return true;
    }

    /// <summary>
    /// Drops the command the run is already working from — the one whose user message is in the
    /// transcript — and unblocks the queue. This is what interrupting means: the attempt is
    /// abandoned rather than kept for a retry, so a Paused or Failed run goes back to Idle and
    /// whatever is queued behind it can start. What the transcript keeps is the user message and,
    /// when the model had produced any, the truncated answer.
    /// </summary>
    public void DropCommitted()
    {
        if (Status == RunStatus.Generating) throw new DomainException("Stop the run before dropping its command.");
        foreach (var committed in _queue.Where(item => item.Stage == QueuedRunStage.UserCommitted).ToArray())
            _queue.Remove(committed);
        Status = RunStatus.Idle;
        Error = null;
        FailureKind = RunFailureKind.None;
        StreamingContent = string.Empty;
        Revision++;
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    public bool RememberOperation(Guid operationId) => _operations.Add(operationId);

    public void Start()
    {
        Status = RunStatus.Generating;
        StreamingContent = string.Empty;
        Error = null;
        FailureKind = RunFailureKind.None;
        Revision++;
    }

    public void Append(string content)
    {
        if (Status != RunStatus.Generating) throw new DomainException("Run is not generating.");
        StreamingContent += content;
        Revision++;
    }

    public void Complete(bool unread)
    {
        Status = RunStatus.Completed;
        HasUnreadResponse = unread;
        StreamingContent = string.Empty;
        FailureKind = RunFailureKind.None;
        Revision++;
    }

    public void Dequeue()
    {
        if (_queue.Count > 0)
        {
            _queue.RemoveAt(0); Revision++;
        }
    }

    public void Remove(Guid id)
    {
        var index = _queue.FindIndex(item => item.Id == id);
        if (index >= 0)
        {
            _queue.RemoveAt(index); Revision++;
        }
    }

    /// <summary>
    /// Releases the live copy of a partial answer once it has been committed as a real message.
    /// Pause()/Fail() deliberately keep <see cref="StreamingContent"/> so the text does not vanish
    /// from the transcript while the answer is only in memory; once it has been persisted, keeping
    /// it would render it twice.
    /// </summary>
    public void ClearStreaming()
    {
        if (StreamingContent.Length == 0) return;
        StreamingContent = string.Empty;
        Revision++;
    }

    public void Update(Guid id, string content)
    {
        var index = _queue.FindIndex(item => item.Id == id);
        if (index >= 0)
        {
            if (string.IsNullOrWhiteSpace(content) && _queue[index].Resources is not { Count: > 0 })
                throw new DomainException("Queued message cannot be empty.");
            _queue[index] = _queue[index] with { Content = content };
            Revision++;
        }
    }

    public void MarkUserCommitted(Guid id)
    {
        var index = _queue.FindIndex(item => item.Id == id);
        if (index >= 0 && _queue[index].Stage != QueuedRunStage.UserCommitted)
        {
            _queue[index] = _queue[index] with { Stage = QueuedRunStage.UserCommitted };
            Revision++;
        }
    }

    public void RebaseFirst(long branchRevision, Guid? explicitParentId = null)
    {
        if (_queue.Count == 0) return;
        var parentMode = _queue[0].ParentBranchId is null ? MessageParentMode.BranchHead : MessageParentMode.Explicit;
        _queue[0] = _queue[0] with
        {
            ParentMode = parentMode,
            ParentMessageId = parentMode == MessageParentMode.Explicit ? explicitParentId : null,
            ReplaceSourceId = null,
            ExpectedBranchRevision = branchRevision,
            Stage = QueuedRunStage.Prepared
        };
        Status = RunStatus.Idle;
        Error = null;
        FailureKind = RunFailureKind.None;
        Revision++;
    }

    public void SkipFailed()
    {
        if (Status != RunStatus.Failed || _queue.Count == 0) return;
        _queue.RemoveAt(0);
        Status = RunStatus.Idle;
        Error = null;
        FailureKind = RunFailureKind.None;
        Revision++;
    }

    // A failure the run cannot be retried from and that offers no recovery action leaves its entry
    // with nowhere to go: StartWorker and ProcessAsync both refuse a Failed run, so the message
    // would sit at the head of the queue until it was skipped by hand. Dropping it keeps the queue
    // honest, and lets whatever was queued behind it start normally.
    //
    // A retryable failure keeps its entry instead, because Retry/Resume needs it: the command is
    // what the next attempt is rebuilt from, and an already committed user message is reused from
    // it (see QueuedRunStage), so the history is not duplicated.
    //
    // The dropped entry takes its error text with it (SkipFailed clears Error), and unread is
    // cleared too: nothing was produced, so flagging it as an unread response would be a false
    // signal. What the user is left with is the answered-less user message in the transcript.
    public void FailUnrecoverable(string error, RunFailureKind failureKind)
    {
        Fail(error, failureKind);
        if (CanRetry) return;
        SkipFailed();
        MarkRead();
    }

    public void Move(Guid id, int position)
    {
        var index = _queue.FindIndex(item => item.Id == id);
        if (index < 0) return;

        var item = _queue[index];
        _queue.RemoveAt(index);
        _queue.Insert(Math.Clamp(position, 0, _queue.Count), item);
        Revision++;
    }

    public void Resume()
    {
        if (Status == RunStatus.Failed && !CanRetry) return;
        if (Status is RunStatus.Paused or RunStatus.Interrupted or RunStatus.Failed)
        {
            Status = RunStatus.Idle;
            Error = null;
            FailureKind = RunFailureKind.None;
            Revision++;
        }
    }

    /// <summary>
    /// Removes everything that has not been sent yet, leaving an in-flight or failed command
    /// alone. "Clear the queue" means exactly the rows the user can see in the queue panel: a
    /// command that is already a message in the transcript is not one of them, and silently
    /// keeping some of what was cleared is what made the old behaviour look broken.
    /// </summary>
    public void ClearPending()
    {
        if (_queue.RemoveAll(item => item.Stage == QueuedRunStage.Prepared) == 0) return;
        Revision++;
    }

    public void Clear()
    {
        _queue.Clear();
        if (Status != RunStatus.Generating)
        {
            Status = RunStatus.Idle;
            Error = null;
            FailureKind = RunFailureKind.None;
        }

        Revision++;
    }

    // Paused/Interrupted/Failed flag unread just like Complete(unread: true) does: the sidebar
    // indicator for all four is gated on HasUnreadResponse (see Home.razor GetRunStatusClass), so
    // MarkRead() clears them the moment the chat is opened — the same way a completed response
    // stops being highlighted once it's been seen, without anyone having to click Resume/Clear.
    public void Pause()
    {
        Status = RunStatus.Paused;
        HasUnreadResponse = true;
        Revision++;
    }

    public void Fail(string error, RunFailureKind failureKind = RunFailureKind.Transient)
    {
        Status = RunStatus.Failed;
        Error = error;
        FailureKind = failureKind;
        HasUnreadResponse = true;
        Revision++;
    }

    public void MarkRead()
    {
        HasUnreadResponse = false;
        Revision++;
    }

    public static ChatRunState Restore(Guid projectId, Guid chatId, Guid branchId, RunStatus status, string streamingContent,
        string? error, RunFailureKind failureKind, bool hasUnreadResponse, long revision,
        IEnumerable<QueuedRunMessage> queue, IEnumerable<Guid> operations)
    {
        var state = new ChatRunState(projectId, chatId, branchId)
        {
            Status = status,
            StreamingContent = streamingContent,
            Error = error,
            FailureKind = failureKind,
            HasUnreadResponse = hasUnreadResponse,
            Revision = revision
        };
        state._queue.AddRange(queue);
        state._operations.UnionWith(operations);
        return state;
    }

    public void RecoverAfterRestart()
    {
        if (Status != RunStatus.Generating) return;
        Status = RunStatus.Interrupted;
        HasUnreadResponse = true;
        Revision++;
    }
}
