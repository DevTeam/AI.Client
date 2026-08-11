namespace Build.Targets;

internal sealed class TestSolutionTarget(IProcessRunner processRunner) : ITestSolutionTarget
{
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        processRunner.RunAsync("Test solution", "dotnet", ["test", "AI.Client.slnx", "--nologo"], cancellationToken);
}
