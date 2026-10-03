// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Host;

using System.Diagnostics;
using Pure.DI;
using Pure.DI.MS;
using Server.CommandLine;

/// <summary>Parses the command line and starts the server it describes.</summary>
internal sealed partial class CommandLineComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Server.CommandLine.Composition")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<ICommandLineApplication>(nameof(Root))
            .Transient<StandaloneCommand, OpenCommand>(Tag.Unique)
            .Transient<HostStatus, BrowserOpener, HostProcess, WebAppLauncher, ServerRunner, HostRunner, HostTray>();
}

/// <summary>The server graph for one run; also ASP.NET's service provider factory.</summary>
internal sealed partial class ServerComposition : ServiceProviderFactory<ServerComposition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            // Two calls on purpose: Pure.DI 2.5.4 crashes (DIE043) on DependsOn("a", "b").
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.AspNetComposition")
            .Hint(Hint.Comments, "Off")
            .Root<Server.Hosting.IAiClientServer>(nameof(Server));
}
