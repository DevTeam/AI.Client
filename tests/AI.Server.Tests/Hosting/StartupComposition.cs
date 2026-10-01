namespace AI.Server.Tests.Hosting;

using System.Diagnostics;
using Pure.DI;
using Pure.DI.MS;

/// <summary>Uses the same ASP.NET service provider bridge as Desktop and Host.</summary>
internal sealed partial class StartupComposition : ServiceProviderFactory<StartupComposition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.Composition");
}
