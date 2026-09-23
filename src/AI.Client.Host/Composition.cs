// ReSharper disable UnusedMember.Local
namespace AI.Client.Host;

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
            .DependsOn("AI.Client.Server.CommandLine.Composition")
            .Bind<IInitializable>(Tag.Unique).As(Lifetime.Singleton).To<StandaloneCommand>()
            .Bind<IServerRunner>().To<ServerRunner>();
}

/// <summary>The server graph for one run; also ASP.NET's service provider factory.</summary>
internal sealed partial class ServerComposition : ServiceProviderFactory<ServerComposition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            // Two calls on purpose: Pure.DI 2.5.4 crashes (DIE043) on DependsOn("a", "b").
            .DependsOn("AI.Client.Contracts.Composition")
            .DependsOn("AI.Client.Server.Composition");
}
