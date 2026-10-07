// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Desktop;

using System.Diagnostics;
using AI.Updates;
using Pure.DI;
using Pure.DI.MS;

/// <summary>Parses the command line and starts the desktop app it describes.</summary>
internal sealed partial class CommandLineComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Server.CommandLine.Composition")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<Server.CommandLine.ICommandLineApplication>(nameof(Root))
            .Transient<DesktopCommand>(Tag.Unique)
            .Transient<DesktopRunner, SharedHostLocator>();
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

/// <summary>The window over the running server.</summary>
internal sealed partial class UiComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<App>(nameof(App))
            .Arg<DesktopStart>("start")
            .Singleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            .Singleton<App>()
            .Transient<UpdateManagerFactory, PublishedUpdateFeed, UpdateInstaller, UpdateInstallationProvider,
                MainWindow, ProcessSignals, DesktopUpdates, JsonWindowPlacementStore, JsonWorkspaceLocationStore,
                JsonThemePreferenceStore, JsonClientSettingsStore, WindowsTaskbarBadge, WindowsFrameTheme, WebView2FileDropBridge>();
}
