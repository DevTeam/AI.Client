// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
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
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .DependsOn("AI.Server.CommandLine.Composition")
            .Root<AI.Server.CommandLine.ICommandLineApplication>(nameof(Root))
            .Transient<DesktopCommand>(Tag.Unique)
            .Transient<DesktopRunner>()
            .Transient<SharedHostLocator>();
}

/// <summary>The server graph for one run; also ASP.NET's service provider factory.</summary>
internal sealed partial class ServerComposition : ServiceProviderFactory<ServerComposition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Comments, "Off")
            // Two calls on purpose: Pure.DI 2.5.4 crashes (DIE043) on DependsOn("a", "b").
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.AspNetComposition")
            .Root<AI.Server.Hosting.IAiClientServer>(nameof(Server));
}

/// <summary>The window over the running server.</summary>
internal sealed partial class UiComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Arg<DesktopStart>("start")
            .Transient<AI.Updates.UpdateManagerFactory, AI.Updates.GitHubUpdateFeed, AI.Updates.UpdateInstaller, AI.Updates.UpdateInstallationProvider>()
            .Singleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            .Singleton<App>()
            .Transient<MainWindow, ProcessSignals, DesktopUpdates, JsonWindowPlacementStore, JsonWorkspaceLocationStore, JsonThemePreferenceStore, JsonClientSettingsStore, WindowsTaskbarBadge,
                WebView2FileDropBridge>()
            .Root<App>(nameof(App));
}
