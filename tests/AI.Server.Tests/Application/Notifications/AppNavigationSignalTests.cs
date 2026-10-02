namespace AI.Application.Tests.Notifications;

using AI.Application.Notifications;
using AI.Contracts.Navigation;
using Shouldly;
using Xunit;

public sealed class AppNavigationSignalTests
{
    [Fact]
    public async Task ShouldReportNoConnectedWindowWithoutWaiting()
    {
        var signal = new AppNavigationSignal();
        var response = await signal.RequestAsync(new AppNavigation(Guid.NewGuid()), CancellationToken.None);
        response.Outcome.ShouldBe("unavailable");
    }

    [Fact]
    public async Task ShouldWaitForTheClaimingWindowAndRejectStaleDecisions()
    {
        var signal = new AppNavigationSignal();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = signal.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var pending = signal.RequestAsync(new AppNavigation(Guid.NewGuid(), Target: "settings", Action: "show"), timeout.Token);
        (await requests.MoveNextAsync()).ShouldBeTrue();
        requests.Current.ExpiresAt.ShouldNotBeNull().ShouldBeInRange(
            DateTimeOffset.UtcNow.AddSeconds(14), DateTimeOffset.UtcNow.AddSeconds(16));
        var requestId = requests.Current.RequestId;
        var owner = Guid.NewGuid();
        pending.IsCompleted.ShouldBeFalse();
        signal.Complete(requestId, new(owner, "applied")).ShouldBeFalse();
        signal.Claim(requestId, owner).ShouldBeTrue();
        signal.Claim(requestId, Guid.NewGuid()).ShouldBeFalse();
        signal.Complete(requestId, new(Guid.NewGuid(), "applied")).ShouldBeFalse();
        signal.Complete(requestId, new(owner, "stopped")).ShouldBeTrue();
        (await pending).Outcome.ShouldBe("stopped");
        signal.Complete(requestId, new(owner, "applied")).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldRespectTheRequestedDeadlineAndExpireWithoutAResponse()
    {
        var signal = new AppNavigationSignal();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = signal.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(200);
        var pending = signal.RequestAsync(new AppNavigation(Guid.NewGuid(), ExpiresAt: expiresAt), timeout.Token);
        (await requests.MoveNextAsync()).ShouldBeTrue();
        requests.Current.ExpiresAt.ShouldBe(expiresAt);
        (await pending).Outcome.ShouldBe("expired");
        signal.Claim(requests.Current.RequestId, Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldRemoveCancelledRequestsAndKeepEveryConcurrentRequest()
    {
        var signal = new AppNavigationSignal();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancel = new CancellationTokenSource();
        await using var requests = signal.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var first = signal.RequestAsync(new AppNavigation(Guid.NewGuid()), cancel.Token);
        var second = signal.RequestAsync(new AppNavigation(Guid.NewGuid()), timeout.Token);
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var firstId = requests.Current.RequestId;
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first);
        signal.Claim(firstId, Guid.NewGuid()).ShouldBeFalse();
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var owner = Guid.NewGuid();
        signal.Claim(requests.Current.RequestId, owner).ShouldBeTrue();
        signal.Complete(requests.Current.RequestId, new(owner, "applied")).ShouldBeTrue();
        (await second).Outcome.ShouldBe("applied");
    }
}
