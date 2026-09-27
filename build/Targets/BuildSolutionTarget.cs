namespace Build.Targets;

internal sealed class BuildSolutionTarget(IProcessRunner processRunner) : IBuildSolutionTarget
{
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        processRunner.RunAsync("Build solution", "dotnet", ["build", "AI.slnx", "--nologo"], cancellationToken);
}
