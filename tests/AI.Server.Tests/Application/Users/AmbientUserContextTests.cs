namespace AI.Application.Tests.Users;

using AI.Application.Users;
using AI.Domain.Users;
using Shouldly;
using Xunit;

public sealed class AmbientUserContextTests
{
    private static readonly UserId Other = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public void CurrentThrowsWhenNoScopeIsActive()
    {
        var context = new AmbientUserContext();

        Should.Throw<InvalidOperationException>(() => context.Current);
    }

    [Fact]
    public void EnterMakesTheUserCurrentUntilDisposed()
    {
        var context = new AmbientUserContext();

        using (context.Enter(UserId.Local))
        {
            context.Current.ShouldBe(UserId.Local);
        }

        Should.Throw<InvalidOperationException>(() => context.Current);
    }

    [Fact]
    public void DisposingANestedScopeRestoresTheOuterUser()
    {
        var context = new AmbientUserContext();

        using (context.Enter(UserId.Local))
        {
            using (context.Enter(Other))
            {
                context.Current.ShouldBe(Other);
            }

            context.Current.ShouldBe(UserId.Local);
        }
    }

    [Fact]
    public void DisposingAScopeTwiceRestoresOnlyOnce()
    {
        var context = new AmbientUserContext();
        using var outer = context.Enter(UserId.Local);
        var inner = context.Enter(Other);

        inner.Dispose();
        inner.Dispose();

        context.Current.ShouldBe(UserId.Local);
    }

    [Fact]
    public async Task TheUserFollowsAwaitAndTaskRun()
    {
        var context = new AmbientUserContext();
        using var scope = context.Enter(Other);

        await Task.Yield();
        var seenByTask = await Task.Run(() => context.Current, TestContext.Current.CancellationToken);

        context.Current.ShouldBe(Other);
        seenByTask.ShouldBe(Other);
    }

    [Fact]
    public async Task AScopeEnteredInOneFlowIsNotSeenByAnother()
    {
        var context = new AmbientUserContext();
        var entered = new TaskCompletionSource();
        var checkedOther = new TaskCompletionSource();
        var token = TestContext.Current.CancellationToken;

        var first = Task.Run(async () =>
        {
            using var scope = context.Enter(Other);
            entered.SetResult();
            await checkedOther.Task.WaitAsync(token);
            return context.Current;
        }, token);
        var second = Task.Run(async () =>
        {
            await entered.Task.WaitAsync(token);
            try
            {
                using var scope = context.Enter(UserId.Local);
                return context.Current;
            }
            finally { checkedOther.SetResult(); }
        }, token);

        (await first).ShouldBe(Other);
        (await second).ShouldBe(UserId.Local);
        Should.Throw<InvalidOperationException>(() => context.Current);
    }

    [Fact]
    public async Task RunWorksForTheGivenUserWhicheverFlowStartsIt()
    {
        var context = new AmbientUserContext();
        using var scope = context.Enter(UserId.Local);

        var seen = await context.Run(Other, async () =>
        {
            await Task.Yield();
            return context.Current;
        });
        await context.Run(Other, () =>
        {
            context.Current.ShouldBe(Other);
            return Task.CompletedTask;
        });

        seen.ShouldBe(Other);
        context.Current.ShouldBe(UserId.Local);
    }

    [Fact]
    public async Task LocalUserDirectoryListsOnlyTheLocalUser()
    {
        var users = await new LocalUserDirectory().ListAsync(TestContext.Current.CancellationToken);

        users.ShouldBe([UserId.Local]);
    }
}
