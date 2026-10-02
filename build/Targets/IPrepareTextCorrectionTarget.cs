namespace Build.Targets;

internal interface IPrepareTextCorrectionTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
