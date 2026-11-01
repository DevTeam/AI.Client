namespace AI.Application.Tests.Tools;

using AI.Application.Tools;
using AI.Contracts.Tools;
using System.Text.Json;
using Shouldly;
using Xunit;

public sealed class SubtaskRunsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ToolRunContext Owner() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
    private static SubtaskCompletion Done(string answer) =>
        new(new("Task", answer, "Test", 1, 0, 0, true, null, [], [new("check", "Passed")], []), []);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task AnyCollectsFinishedWorkWhileAllAndSnapshotRetainRunningHandles()
    {
        await using var runs = new SubtaskRuns();
        var owner = Owner();
        await using var scope = runs.Begin(owner, Token);
        var gate = Gate();
        var slow = runs.Start(owner, "slow", async (_, token) => { await gate.Task.WaitAsync(token); return Done("slow"); });
        var fast = runs.Start(owner, "fast", (_, _) => Task.FromResult(Done("fast")));
        var first = await runs.WaitAsync(owner, [slow.TaskId, fast.TaskId], false, 10000, null, Token);
        first.Tasks[0].Result.ShouldBeNull();
        first.Tasks[1].Result!.Answer.ShouldBe("fast");
        var snapshot = await runs.WaitAsync(owner, [slow.TaskId], true, 0, null, Token);
        snapshot.Tasks.ShouldHaveSingleItem().Result.ShouldBeNull();
        snapshot.TimedOut.ShouldBeFalse();
        (await runs.WaitAsync(owner, [slow.TaskId], true, 10, null, Token)).TimedOut.ShouldBeTrue();
        var all = runs.WaitAsync(owner, [slow.TaskId, fast.TaskId], true, 10000, null, Token);
        all.IsCompleted.ShouldBeFalse();
        gate.SetResult();
        (await all).Tasks.ShouldAllBe(task => task.Status == "completed");
    }

    [Fact]
    public async Task CollectionIsReplayableAndOnlyConsumedTerminalResultsAreAcknowledged()
    {
        await using var runs = new SubtaskRuns();
        var owner = Owner();
        await using var scope = runs.Begin(owner, Token);
        var calls = 0;
        var task = runs.Start(owner, "one", (_, _) => { calls++; return Task.FromResult(Done("answer")); });
        var result = await runs.WaitAsync(owner, [task.TaskId], true, 10000, null, Token);
        runs.Outstanding(owner).Count.ShouldBe(1);
        runs.Acknowledge(owner, JsonSerializer.SerializeToElement(result, JsonSerializerOptions.Web));
        runs.Outstanding(owner).ShouldBeEmpty();
        (await runs.WaitAsync(owner, [task.TaskId], true, 0, null, Token)).Tasks[0].Result!.Answer.ShouldBe("answer");
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task CancelledWaitDoesNotCancelTheTaskAndStopSelectionIsValidatedAtomically()
    {
        await using var runs = new SubtaskRuns();
        var owner = Owner();
        await using var scope = runs.Begin(owner, Token);
        var started = Gate();
        var cancelled = Gate();
        var task = runs.Start(owner, "one", async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }
            return Done("unreachable");
        });
        await started.Task.WaitAsync(Token);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = runs.WaitAsync(owner, [task.TaskId], true, 10000, null, wait.Token);
        await wait.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        cancelled.Task.IsCompleted.ShouldBeFalse();
        await Should.ThrowAsync<ArgumentException>(() => runs.StopAsync(owner, [task.TaskId, "foreign"], Token));
        cancelled.Task.IsCompleted.ShouldBeFalse();
        (await runs.StopAsync(owner, [task.TaskId], Token)).Tasks[0].Status.ShouldBe("cancelled");
        cancelled.Task.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task ParentScopeDrainsCancellationAndExpiresItsHandlesBeforeTheNextRun()
    {
        await using var runs = new SubtaskRuns();
        var owner = Owner();
        var scope = runs.Begin(owner, Token);
        var started = Gate();
        var draining = Gate();
        var release = Gate();
        var task = runs.Start(owner, "one", async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { draining.SetResult(); await release.Task.WaitAsync(Token); }
            return Done("unreachable");
        });
        await started.Task.WaitAsync(Token);
        var closing = scope.DisposeAsync().AsTask();
        await draining.Task.WaitAsync(Token);
        closing.IsCompleted.ShouldBeFalse();
        release.SetResult();
        await closing;
        await using var next = runs.Begin(owner, Token);
        await Should.ThrowAsync<ArgumentException>(() => runs.WaitAsync(owner, [task.TaskId], true, 0, null, Token));
    }

    [Fact]
    public async Task ParentCancellationStopsChildrenEvenWithoutAWait()
    {
        await using var runs = new SubtaskRuns();
        var owner = Owner();
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var scope = runs.Begin(owner, parent.Token);
        var task = runs.Start(owner, "one", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Done("unreachable");
        });
        await parent.CancelAsync();
        (await runs.WaitAsync(owner, [task.TaskId], true, 10000, null, Token)).Tasks[0].Status.ShouldBe("cancelled");
        Should.Throw<InvalidOperationException>(() => runs.Start(owner, "late", (_, _) => Task.FromResult(Done("late"))));
    }

    [Fact]
    public async Task SaturatedParentsQueueRootWorkButRefuseNestedWorkWithoutDeadlock()
    {
        await using var runs = new SubtaskRuns();
        var owner = Owner();
        await using var scope = runs.Begin(owner, Token);
        var full = Gate();
        var release = Gate();
        var count = 0;
        var handles = Enumerable.Range(0, 32).Select(index => runs.Start(owner, $"parent {index}", async (_, token) =>
        {
            if (Interlocked.Increment(ref count) == 32) full.SetResult();
            await release.Task.WaitAsync(token);
            return Done("parent");
        })).ToArray();
        await full.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var queued = runs.Start(owner, "queued", (_, _) => Task.FromResult(Done("queued")));
        (await runs.WaitAsync(owner, [queued.TaskId], true, 0, null, Token)).Tasks[0].Status.ShouldBe("queued");
        var nestedOwner = Owner() with { IsSubtask = true, SubtaskDepth = 1 };
        await using var nested = runs.Begin(nestedOwner, Token);
        var child = runs.Start(nestedOwner, "nested", (_, _) => Task.FromResult(Done("nested")));
        var failed = (await runs.WaitAsync(nestedOwner, [child.TaskId], true, 10000, null, Token)).Tasks[0];
        failed.Status.ShouldBe("failed");
        failed.Result!.Error.ShouldNotBeNull().ShouldContain("slot");
        release.SetResult();
        (await runs.WaitAsync(owner, [.. handles.Select(task => task.TaskId), queued.TaskId], true, 10000, null, Token))
            .Tasks.ShouldAllBe(task => task.Status == "completed");
    }
}
