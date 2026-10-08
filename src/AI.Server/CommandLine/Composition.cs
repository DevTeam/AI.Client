// DI guide: [Pure.DI conventions](../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Server.CommandLine;

using System.CommandLine;
using System.Diagnostics;
using Pure.DI;

/// <summary>
/// The command line shared by every executable that hosts the server. Executables link this file,
/// build on it with <c>DependsOn("AI.Server.CommandLine.Composition")</c> and add their own
/// <see cref="IInitializable"/> commands, which register themselves on the root command.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            // The command line resolves ServerCommandLine, which reaches the file system; without
            // this the command-line graph never reaches the setup that binds it.
            .DependsOn("AI.Contracts.Composition")
            .Hint(Hint.Comments, "Off")
            .Arg<string[]>("args")
            .PerResolve(() => new RootCommand())
            .Transient<ServerCommandLine, CommandLineApplication>();
}
