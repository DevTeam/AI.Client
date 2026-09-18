namespace Build.Targets;

internal sealed record RunBothOptions(
    string? HostUrls,
    string? WebUrls,
    string? CorsOrigins,
    string? Environment);

internal interface IRunBothTarget
{
    Task<int> RunAsync(RunBothOptions options, CancellationToken cancellationToken);
}
