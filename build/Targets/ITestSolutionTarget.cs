namespace Build.Targets;

internal interface ITestSolutionTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
