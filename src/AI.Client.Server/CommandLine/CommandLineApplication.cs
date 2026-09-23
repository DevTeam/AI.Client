namespace AI.Client.Server.CommandLine;

using System.CommandLine;

public sealed class CommandLineApplication(
    string[] args,
    RootCommand rootCommand,
    IEnumerable<IInitializable> initializables)
    : ICommandLineApplication
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        foreach (var initializable in initializables)
        {
            await initializable.InitializeAsync(cancellationToken);
        }

        // Shutdown on Ctrl+C belongs to whatever the action hosts (ASP.NET's console lifetime drains
        // runs before it stops), so System.CommandLine must not cut it short after its own timeout.
        var configuration = new InvocationConfiguration { ProcessTerminationTimeout = null };
        return await rootCommand.Parse(args).InvokeAsync(configuration, cancellationToken);
    }
}
