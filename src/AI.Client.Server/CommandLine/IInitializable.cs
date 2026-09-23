namespace AI.Client.Server.CommandLine;

/// <summary>
/// A part of the command line that registers itself — its options, subcommands or action — on the
/// shared <see cref="System.CommandLine.RootCommand"/> before the arguments are parsed.
/// </summary>
public interface IInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
