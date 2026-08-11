namespace Build.Targets;

internal interface IProcessRunner
{
    Task<int> RunAsync(
        string operation,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
