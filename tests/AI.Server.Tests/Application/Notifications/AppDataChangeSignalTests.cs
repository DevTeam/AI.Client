namespace AI.Application.Tests.Notifications;

using AI.Application.Notifications;
using Shouldly;
using Xunit;

public sealed class AppDataChangeSignalTests
{
    [Fact]
    public async Task ShouldReachEverySubscriber()
    {
        var signal = new AppDataChangeSignal();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = signal.SubscribeAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var second = signal.SubscribeAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            signal.Notify();

            (await first.MoveNextAsync()).ShouldBeTrue();
            (await second.MoveNextAsync()).ShouldBeTrue();
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    [Fact]
    public async Task ShouldNotLoseAChangeThatLandsBeforeEnumerationStarts()
    {
        var signal = new AppDataChangeSignal();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Subscribing is the moment a listener starts counting, not the moment it gets around to
        // reading — otherwise a change made right afterwards would never be seen.
        var changes = signal.SubscribeAsync(stop.Token);
        signal.Notify();

        var enumerator = changes.GetAsyncEnumerator(stop.Token);
        try { (await enumerator.MoveNextAsync()).ShouldBeTrue(); }
        finally { await enumerator.DisposeAsync(); }
    }

    [Fact]
    public async Task ShouldCollapseABurstIntoOneWakeUp()
    {
        var signal = new AppDataChangeSignal();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changes = signal.SubscribeAsync(stop.Token);
        for (var index = 0; index < 10; index++) signal.Notify();

        var enumerator = changes.GetAsyncEnumerator(stop.Token);
        try
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            // Ten changes, one wake-up carrying the latest count: a reader that was busy re-reads
            // once rather than ten times, and still knows it is looking at the newest state.
            enumerator.Current.ShouldBe(10);
        }
        finally
        {
            await stop.CancelAsync();
            try { await enumerator.DisposeAsync(); } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public void ShouldIgnoreAChangeNobodyIsListeningFor()
    {
        var signal = new AppDataChangeSignal();

        Should.NotThrow(signal.Notify);
    }
}
