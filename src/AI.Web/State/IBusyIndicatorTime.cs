namespace AI.Web.State;

/// <summary>Monotonic time and delays used by a busy indicator.</summary>
public interface IBusyIndicatorTime
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long timestamp);
    Task DelayAsync(TimeSpan delay);
}

public sealed class SystemBusyIndicatorTime : IBusyIndicatorTime
{
    public long GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long timestamp) => System.Diagnostics.Stopwatch.GetElapsedTime(timestamp);
    public Task DelayAsync(TimeSpan delay) => Task.Delay(delay);
}
