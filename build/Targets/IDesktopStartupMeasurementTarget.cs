namespace Build.Targets;

internal interface IDesktopStartupMeasurementTarget
{
    Task<int> RunAsync(
        string scenario,
        int runs,
        string? dataDirectory,
        bool keepCopies,
        CancellationToken cancellationToken);
}
