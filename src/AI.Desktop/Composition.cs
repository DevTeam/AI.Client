// ReSharper disable UnusedMember.Local
namespace AI.Desktop;

using System.Diagnostics;
using Pure.DI;
using Pure.DI.MS;

/// <summary>Parses the command line and starts the desktop app it describes.</summary>
internal sealed partial class CommandLineComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .DependsOn("AI.Server.CommandLine.Composition")
            .Singleton<DesktopCommand>(Tag.Unique)
            .Transient<DesktopRunner>()
            .Transient<SharedHostLocator>();
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

/// <summary>The window over the running server.</summary>
internal sealed partial class UiComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Arg<DesktopStart>("start")
            .Singleton<App, MainWindow, ProcessSignals, JsonWindowPlacementStore, JsonWorkspaceLocationStore, JsonThemePreferenceStore, WindowsTaskbarBadge,
                WebView2FileDropBridge>()
            .Root<App>(nameof(App));
}
