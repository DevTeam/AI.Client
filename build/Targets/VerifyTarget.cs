namespace Build.Targets;

internal sealed class VerifyTarget(
    IBuildSolutionTarget buildSolutionTarget,
    ITestSolutionTarget testSolutionTarget) : IVerifyTarget
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var buildResult = await buildSolutionTarget.RunAsync(cancellationToken);
        return buildResult == 0
            ? await testSolutionTarget.RunAsync(cancellationToken)
            : buildResult;
    }
}
