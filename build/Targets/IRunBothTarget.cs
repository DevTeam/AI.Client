namespace Build.Targets;

internal sealed record RunBothOptions(
    string? HostUrls,
    string? WebUrls,
    string? CorsOrigins,
    string? Environment,
    bool PublicWeb = false);

internal interface IRunBothTarget
{
    Task<int> RunAsync(RunBothOptions options, CancellationToken cancellationToken);
}
