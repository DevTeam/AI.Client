namespace Build.Targets;

internal sealed class TestSolutionTarget(IProcessRunner processRunner) : ITestSolutionTarget
{
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        processRunner.RunAsync("Test units", "dotnet",
            ["test", "AI.slnx", "--filter-not-trait", "Category=Integration",
                "--filter-not-trait", "Category=Slow"], cancellationToken);

    public Task<int> RunAllAsync(CancellationToken cancellationToken) =>
        processRunner.RunAsync("Test all", "dotnet", ["test", "AI.slnx"], cancellationToken);
}
