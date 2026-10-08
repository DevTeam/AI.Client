namespace AI.Web.Tests.State;

using AI.Web.State;
using Shouldly;
using Xunit;

public sealed class DelayedBusyIndicatorTests
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(160);

    private readonly ManualBusyIndicatorTime _time = new();
    private int _notifications;

    private DelayedBusyIndicator CreateIndicator() => new(_time,
        () => { _notifications++; return Task.CompletedTask; }, ShowDelay, MinimumVisible);

    [Fact]
    public async Task DoesNotShowWhileTheLoadIsStillInsideTheDelayWindow()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();

        await _time.AdvanceAsync(ShowDelay / 2);

        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    [Fact]
    public async Task NeverShowsForALoadThatFinishesInsideTheDelayWindow()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();
        indicator.End();

        await _time.AdvanceAsync(ShowDelay + MinimumVisible);

        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    [Fact]
    public async Task ShowsOnceTheLoadOutlivesTheDelay()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();

        await _time.AdvanceAsync(ShowDelay);

        await WaitForAsync(() => indicator.IsVisible);
        indicator.IsVisible.ShouldBeTrue();
        _notifications.ShouldBe(1);
    }

    [Fact]
    public async Task KeepsThePlaceholderUpForItsMinimumAfterTheLoadEnds()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();
        await _time.AdvanceAsync(ShowDelay);
        await WaitForAsync(() => indicator.IsVisible);
        indicator.End();

        await _time.AdvanceAsync(MinimumVisible - TimeSpan.FromTicks(1));
        indicator.IsVisible.ShouldBeTrue();
        await _time.AdvanceAsync(TimeSpan.FromTicks(1));
        await WaitForAsync(() => !indicator.IsVisible);
        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(2);
    }

    [Fact]
    public async Task ASecondLoadKeepsTheVisiblePlaceholderInsteadOfBlinkingIt()
    {
        using var indicator = CreateIndicator();
        indicator.Begin();
        await _time.AdvanceAsync(ShowDelay);
        await WaitForAsync(() => indicator.IsVisible);
        indicator.End();
        indicator.Begin();

        await _time.AdvanceAsync(MinimumVisible * 2);
        indicator.IsVisible.ShouldBeTrue();
        indicator.End();
        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(2);
    }

    [Fact]
    public async Task EndWithoutBeginDoesNothing()
    {
        using var indicator = CreateIndicator();
        indicator.End();

        await _time.AdvanceAsync(ShowDelay);

        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    [Fact]
    public async Task StopsNotifyingOnceDisposed()
    {
        var indicator = CreateIndicator();
        indicator.Begin();
        indicator.Dispose();

        await _time.AdvanceAsync(ShowDelay * 3);

        indicator.IsVisible.ShouldBeFalse();
        _notifications.ShouldBe(0);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1000 && !condition(); attempt++) await Task.Yield();
        condition().ShouldBeTrue("the scheduled transition should complete without waiting on wall-clock time");
    }

    private sealed class ManualBusyIndicatorTime : IBusyIndicatorTime
    {
        private long _ticks;
        private readonly List<(long Due, TaskCompletionSource Completion)> _scheduled = [];

        public long GetTimestamp() => _ticks;
        public TimeSpan GetElapsedTime(long timestamp) => TimeSpan.FromTicks(_ticks - timestamp);

        public Task DelayAsync(TimeSpan delay)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _scheduled.Add((_ticks + delay.Ticks, completion));
            return completion.Task;
        }

        public async Task AdvanceAsync(TimeSpan elapsed)
        {
            _ticks += elapsed.Ticks;
            while (true)
            {
                var due = _scheduled.Where(item => item.Due <= _ticks).ToArray();
                if (due.Length == 0) break;
                foreach (var item in due)
                {
                    _scheduled.Remove(item);
                    item.Completion.SetResult();
                }
                await Task.Yield();
            }
            await Task.Yield();
        }
    }
}
