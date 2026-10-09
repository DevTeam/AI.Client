namespace Build.Targets;

internal interface IProfileDesktopTarget
{
    Task<int> RunAsync(string profiler, string? dataDirectory, CancellationToken cancellationToken);
}
