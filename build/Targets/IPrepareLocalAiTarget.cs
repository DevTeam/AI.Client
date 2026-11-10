namespace Build.Targets;

internal interface IPrepareLocalAiTarget
{
    Task<string> RunAsync(string runtime, CancellationToken cancellationToken);
}
