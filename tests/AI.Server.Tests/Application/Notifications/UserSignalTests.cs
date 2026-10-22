namespace AI.Application.Tests.Notifications;

using AI.Application.Notifications;
using AI.Application.Users;
using AI.Contracts.Navigation;
using AI.Domain.Users;
using Shouldly;
using Xunit;

/// <summary>A signal raised for one user is neither delivered to nor answerable by another.</summary>
public sealed class UserSignalTests
{
    private static readonly UserId Other = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
    private readonly AmbientUserContext _users = new();

    [Fact]
    public async Task ChangesReachOnlyTheirOwnUsersSubscribers()
    {
        var signal = new AppDataChangeSignal(_users);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IAsyncEnumerator<long> local, other;
        using (_users.Enter(UserId.Local)) local = signal.SubscribeAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        using (_users.Enter(Other)) other = signal.SubscribeAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            using (_users.Enter(UserId.Local)) signal.Notify();

            (await local.MoveNextAsync()).ShouldBeTrue();
            var otherNext = other.MoveNextAsync().AsTask();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            otherNext.IsCompleted.ShouldBeFalse();
            await stop.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(otherNext);
        }
        finally
        {
            await local.DisposeAsync();
            await other.DisposeAsync();
        }
    }

    [Fact]
    public void NotifyingWithoutAUserFails() =>
        Should.Throw<InvalidOperationException>(() => new AppDataChangeSignal(_users).Notify());

    [Fact]
    public async Task NavigationIsUnavailableWithoutTheUsersOwnWindow()
    {
        var signal = new AppNavigationSignal(_users);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IAsyncEnumerator<AppNavigation> otherWindow;
        using (_users.Enter(Other)) otherWindow = signal.SubscribeAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            using var scope = _users.Enter(UserId.Local);
            (await signal.RequestAsync(new AppNavigation(Guid.NewGuid()), stop.Token)).Outcome.ShouldBe("unavailable");
        }
        finally { await otherWindow.DisposeAsync(); }
    }

    [Fact]
    public async Task AnotherUserCannotClaimOrAnswerARequest()
    {
        var signal = new AppNavigationSignal(_users);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<AppNavigationResponse> pending;
        await using var requests = Subscribe(signal, UserId.Local, stop.Token);
        using (_users.Enter(UserId.Local)) pending = signal.RequestAsync(new AppNavigation(Guid.NewGuid()), stop.Token);
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var requestId = requests.Current.RequestId;
        var window = Guid.NewGuid();

        using (_users.Enter(Other))
        {
            signal.Claim(requestId, window).ShouldBeFalse();
            signal.Complete(requestId, new(window, "applied")).ShouldBeFalse();
        }

        using (_users.Enter(UserId.Local))
        {
            signal.Claim(requestId, window).ShouldBeTrue();
            signal.Complete(requestId, new(window, "applied")).ShouldBeTrue();
        }

        (await pending).Outcome.ShouldBe("applied");
    }

    private IAsyncEnumerator<AppNavigation> Subscribe(AppNavigationSignal signal, UserId user, CancellationToken token)
    {
        using var scope = _users.Enter(user);
        return signal.SubscribeAsync(token).GetAsyncEnumerator(token);
    }
}
