namespace Build.Targets;

internal interface IVerifyTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
