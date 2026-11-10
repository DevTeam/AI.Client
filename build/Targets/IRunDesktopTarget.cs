namespace Build.Targets;

internal interface IRunDesktopTarget
{
    Task<int> RunAsync(string? dataDirectory, bool localAi, CancellationToken cancellationToken);
}
