using AI.Client.Domain.Runs;
using Shouldly;
using Xunit;

namespace AI.Client.Domain.Tests.Chats;

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
    public void ShouldRestoreGeneratingRunAsInterrupted()
    {
        var state = ChatRunState.Restore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RunStatus.Generating, "Partial", null, false, 4, [], []);

        state.Status.ShouldBe(RunStatus.Interrupted);
        state.StreamingContent.ShouldBe("Partial");
        state.Revision.ShouldBe(4);
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
