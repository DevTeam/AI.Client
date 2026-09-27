namespace Build.Targets;

internal sealed class PublishTarget(IProcessRunner processRunner) : IPublishTarget
{
    public Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            "Publish host",
            "dotnet",
            ["publish", "src/AI.Host/AI.Host.csproj", "--nologo", "--output", outputDirectory],
            cancellationToken);
}
