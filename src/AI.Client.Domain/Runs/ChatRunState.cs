using AI.Client.Domain.Common;
// ReSharper disable InvertIf

namespace AI.Client.Domain.Runs;

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

    public bool CanRetry => FailureKind is RunFailureKind.None or RunFailureKind.Transient or RunFailureKind.Storage;

    public bool HasUnreadResponse { get; private set; }

    public long Revision { get; private set; }

    public IReadOnlyList<QueuedRunMessage> Queue => _queue;

    public IReadOnlySet<Guid> Operations => _operations;

    public bool Enqueue(Guid operationId, QueuedRunMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Content)) throw new DomainException("Queued message cannot be empty.");
        if (!_operations.Add(operationId)) return false;
        _queue.Add(message); Revision++; return true;
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

    public void Update(Guid id, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new DomainException("Queued message cannot be empty.");
        var index = _queue.FindIndex(item => item.Id == id);
        if (index >= 0)
        {
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
