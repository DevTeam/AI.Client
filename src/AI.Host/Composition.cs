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
            .Hint(Hint.Resolve, "Off")
            .DependsOn("AI.Server.CommandLine.Composition")
            .Singleton<StandaloneCommand>(Tag.Unique)
            .Transient<ServerRunner>();
}

/// <summary>The server graph for one run; also ASP.NET's service provider factory.</summary>
internal sealed partial class ServerComposition : ServiceProviderFactory<ServerComposition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            // Two calls on purpose: Pure.DI 2.5.4 crashes (DIE043) on DependsOn("a", "b").
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.Composition");
}
