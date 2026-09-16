namespace AI.Client.Web.Tests.State;

using System.Diagnostics;
using AI.Client.Web.State;
using Shouldly;
using Xunit;

// Timing-based by nature, so the windows are short but the assertions are one-sided: each one
// waits for a transition that must happen, or watches a window in which one must not. Real
// clocks, because the indicator's whole job is to be slower than the render loop.
public class DelayedBusyIndicatorTests
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(160);

    private int _notifications;

    private DelayedBusyIndicator CreateIndicator() => new(
        () => { _notifications++; return Task.CompletedTask; },
        ShowDelay,
        MinimumVisible);

    [Fact]
    public async Task DoesNotShowWhileTheLoadIsStillInsideTheDelayWindow()
    {
        using var indicator = CreateIndicator();

        indicator.Begin();

        indicator.IsVisible.ShouldBeFalse();
        await Task.Delay(ShowDelay / 2, TestContext.Current.CancellationToken);
        indicator.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task NeverShowsForALoadThatFinishesInsideTheDelayWindow()
    {
        using var indicator = CreateIndicator();

        indicator.Begin();
        indicator.End();

        // Past the point where an undamped flag would have flashed the placeholder.
        await Task.Delay(ShowDelay + MinimumVisible, TestContext.Current.CancellationToken);
        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    [Fact]
    public async Task ShowsOnceTheLoadOutlivesTheDelay()
    {
        using var indicator = CreateIndicator();

        indicator.Begin();

        await WaitForAsync(() => indicator.IsVisible);
        _notifications.ShouldBe(1);
    }

    [Fact]
    public async Task KeepsThePlaceholderUpForItsMinimumAfterTheLoadEnds()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();
        await WaitForAsync(() => indicator.IsVisible);

        var shownAt = Stopwatch.GetTimestamp();
        indicator.End();

        indicator.IsVisible.ShouldBeTrue();
        await WaitForAsync(() => !indicator.IsVisible);
        Stopwatch.GetElapsedTime(shownAt).ShouldBeGreaterThanOrEqualTo(MinimumVisible - TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public async Task ASecondLoadKeepsTheVisiblePlaceholderInsteadOfBlinkingIt()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();
        await WaitForAsync(() => indicator.IsVisible);

        indicator.End();
        indicator.Begin();

        // The hide scheduled by End must notice that a new load owns the placeholder now.
        await Task.Delay(MinimumVisible * 2, TestContext.Current.CancellationToken);
        indicator.IsVisible.ShouldBeTrue();

        indicator.End();
        await WaitForAsync(() => !indicator.IsVisible);
    }

    [Fact]
    public async Task EndWithoutBeginDoesNothing()
    {
        using var indicator = CreateIndicator();

        indicator.End();

        await Task.Delay(ShowDelay, TestContext.Current.CancellationToken);
        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    [Fact]
    public async Task StopsNotifyingOnceDisposed()
    {
        var indicator = CreateIndicator();
        indicator.Begin();
        indicator.Dispose();

        await Task.Delay(ShowDelay * 3, TestContext.Current.CancellationToken);
        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(deadline) < TimeSpan.FromSeconds(5))
        {
            if (condition()) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue("the indicator never reached the expected state");
    }
}
