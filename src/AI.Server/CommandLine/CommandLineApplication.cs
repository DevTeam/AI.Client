namespace AI.Server.CommandLine;

using System.CommandLine;

public sealed class CommandLineApplication(
    string[] args,
    RootCommand rootCommand,
    IEnumerable<IInitializable> initializables)
    : ICommandLineApplication
{
    // Shutdown on Ctrl+C belongs to whatever the action hosts (ASP.NET's console lifetime drains
    // runs before it stops), so System.CommandLine must not cut it short after its own timeout.
    private readonly InvocationConfiguration _configuration = new() { ProcessTerminationTimeout = null };

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        foreach (var initializable in initializables)
        {
            await initializable.InitializeAsync(cancellationToken);
        }

        return await rootCommand.Parse(args).InvokeAsync(_configuration, cancellationToken);
    }

    public int Run()
    {
        foreach (var initializable in initializables)
        {
            // Registration only adds options and actions to the root command; nothing here waits.
            initializable.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return rootCommand.Parse(args).Invoke(_configuration);
    }
}
