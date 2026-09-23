namespace AI.Client.Domain.Tests.Chats;

using Runs;
using Shouldly;
using Xunit;

public class ChatRunStateTests
{
    [Fact]
    public void ShouldIgnoreRepeatedOperation()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var operationId = Guid.NewGuid();
        var message = new QueuedRunMessage(Guid.NewGuid(), "Hello", DateTimeOffset.UtcNow);

        state.Enqueue(operationId, message).ShouldBeTrue();
        state.Enqueue(operationId, message).ShouldBeFalse();
        state.Queue.Count.ShouldBe(1);
    }

    [Fact]
    public void ShouldRecoverGeneratingRunOnlyOnExplicitRestart()
    {
        var state = ChatRunState.Restore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RunStatus.Generating,
            "Partial", null, RunFailureKind.None, false, 4, [], []);

        state.Status.ShouldBe(RunStatus.Generating);
        state.RecoverAfterRestart();
        state.Status.ShouldBe(RunStatus.Interrupted);
        state.StreamingContent.ShouldBe("Partial");
        state.Revision.ShouldBe(5);
    }

    [Fact]
    public void ShouldEditReorderAndRemoveQueuedMessages()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var first = new QueuedRunMessage(Guid.NewGuid(), "First", DateTimeOffset.UtcNow);
        var second = new QueuedRunMessage(Guid.NewGuid(), "Second", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), first);
        state.Enqueue(Guid.NewGuid(), second);
        state.Update(second.Id, "Edited");
        state.Move(second.Id, 0);
        state.Remove(first.Id);
        state.Queue.ShouldHaveSingleItem().ShouldBe(second with { Content = "Edited" });
    }

    [Fact]
    public void ShouldResumePausedQueue()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        state.Enqueue(Guid.NewGuid(), new QueuedRunMessage(Guid.NewGuid(), "Message", DateTimeOffset.UtcNow));
        state.Pause();
        state.Resume();
        state.Status.ShouldBe(RunStatus.Idle);
        state.Queue.Count.ShouldBe(1);
    }

    [Fact]
    public void ShouldRequireExplicitRecoveryForStructuralFailure()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        state.Enqueue(Guid.NewGuid(), new QueuedRunMessage(Guid.NewGuid(), "Message", DateTimeOffset.UtcNow,
            MessageParentMode.Explicit, Guid.NewGuid(), Guid.NewGuid()));
        state.Fail("Missing parent", RunFailureKind.ParentMissing);

        state.Resume();
        state.Status.ShouldBe(RunStatus.Failed);
        state.CanRetry.ShouldBeFalse();

        state.RebaseFirst(7);
        state.Status.ShouldBe(RunStatus.Idle);
        state.Queue[0].ParentMode.ShouldBe(MessageParentMode.BranchHead);
        state.Queue[0].ReplaceSourceId.ShouldBeNull();

        state.Fail("Still invalid", RunFailureKind.BranchChanged);
        state.SkipFailed();
        state.Status.ShouldBe(RunStatus.Idle);
        state.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldDropUnrecoverableFailureFromQueue()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var next = new QueuedRunMessage(Guid.NewGuid(), "Next", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), new QueuedRunMessage(Guid.NewGuid(), "Message", DateTimeOffset.UtcNow,
            MessageParentMode.Explicit, Guid.NewGuid(), Guid.NewGuid()));
        state.Enqueue(Guid.NewGuid(), next);

        state.FailUnrecoverable("Missing parent", RunFailureKind.ParentMissing);

        // The head is gone rather than stuck: it could never run again and offered no recovery,
        // so the message queued behind it is now the head and can proceed.
        state.Status.ShouldBe(RunStatus.Idle);
        state.Queue.ShouldHaveSingleItem().ShouldBe(next);
        state.Error.ShouldBeNull();
        state.FailureKind.ShouldBe(RunFailureKind.None);
        state.HasUnreadResponse.ShouldBeFalse();
        state.CanRetry.ShouldBeTrue();
    }

    [Fact]
    public void ShouldKeepRetryableFailureInQueue()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var message = new QueuedRunMessage(Guid.NewGuid(), "Message", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), message);

        state.FailUnrecoverable("Endpoint failed", RunFailureKind.Transient);

        // A retryable failure keeps its entry: Retry/Resume rebuilds the request from it and
        // reuses the already committed user message, so dropping it would lose the command.
        state.Status.ShouldBe(RunStatus.Failed);
        state.Queue.ShouldHaveSingleItem().ShouldBe(message);
        state.Error.ShouldBe("Endpoint failed");
        state.FailureKind.ShouldBe(RunFailureKind.Transient);
        state.HasUnreadResponse.ShouldBeTrue();
        state.CanRetry.ShouldBeTrue();
    }

    [Fact]
    public void ShouldClearOnlyMessagesThatHaveNotBeenSent()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var sent = new QueuedRunMessage(Guid.NewGuid(), "Sent", DateTimeOffset.UtcNow);
        var waiting = new QueuedRunMessage(Guid.NewGuid(), "Waiting", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), sent);
        state.Enqueue(Guid.NewGuid(), waiting);
        state.MarkUserCommitted(sent.Id);
        state.Start();
        state.Pause();

        state.ClearPending();

        // Exactly the rows the queue panel offers to clear, and no others: the command that is
        // already a message in the transcript is not one of them.
        state.Queue.ShouldHaveSingleItem().Id.ShouldBe(sent.Id);
        state.Status.ShouldBe(RunStatus.Paused);
    }

    [Fact]
    public void ShouldDropTheCommittedCommandAndUnblockTheQueue()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var interrupted = new QueuedRunMessage(Guid.NewGuid(), "Interrupted", DateTimeOffset.UtcNow);
        var next = new QueuedRunMessage(Guid.NewGuid(), "Next", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), interrupted);
        state.Enqueue(Guid.NewGuid(), next);
        state.MarkUserCommitted(interrupted.Id);
        state.Start();
        state.Append("Half an answer");
        state.Fail("Endpoint failed");

        state.DropCommitted();

        state.Queue.ShouldHaveSingleItem().ShouldBe(next);
        state.Status.ShouldBe(RunStatus.Idle);
        state.Error.ShouldBeNull();
        state.StreamingContent.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldRefuseToDropTheCommandOfARunningGeneration()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var message = new QueuedRunMessage(Guid.NewGuid(), "Message", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), message);
        state.MarkUserCommitted(message.Id);
        state.Start();

        Should.Throw<Common.DomainException>(() => state.DropCommitted());
        state.Queue.ShouldHaveSingleItem();
    }

    [Fact]
    public void ShouldKeepPartialAnswerUntilItHasBeenPersisted()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        state.Enqueue(Guid.NewGuid(), new QueuedRunMessage(Guid.NewGuid(), "Message", DateTimeOffset.UtcNow));
        state.Start();
        state.Append("Half an answer");

        // Pause keeps it so the transcript can still render it from memory; clearing it is the
        // explicit step the worker takes once the text is a real message.
        state.Pause();
        state.StreamingContent.ShouldBe("Half an answer");

        state.ClearStreaming();
        state.StreamingContent.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldNotRemoveNextQueuedMessageWhenCurrentRunCompletes()
    {
        var state = new ChatRunState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var current = new QueuedRunMessage(Guid.NewGuid(), "Current", DateTimeOffset.UtcNow);
        var next = new QueuedRunMessage(Guid.NewGuid(), "Next", DateTimeOffset.UtcNow);
        state.Enqueue(Guid.NewGuid(), current);
        state.Enqueue(Guid.NewGuid(), next);
        state.Dequeue();
        state.Start();

        state.Complete(false);

        state.Queue.ShouldHaveSingleItem().ShouldBe(next);
    }
}
