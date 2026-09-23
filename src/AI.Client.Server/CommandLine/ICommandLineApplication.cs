namespace AI.Client.Server.CommandLine;

/// <summary>An executable's entry point: parses the arguments and runs what they ask for.</summary>
public interface ICommandLineApplication
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
