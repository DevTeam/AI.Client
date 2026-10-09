namespace AI.Desktop;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

/// <summary>
/// The process's startup timeline: when the server answered, when the window existed and when the
/// web view finished its first navigation. One marker line is written once to standard output, so a
/// measuring harness that redirects the process's output turns a run into comparable numbers:
/// <c>Startup: desktop ready mode=embedded	412	690	1840</c>. A phase that never happened is a
/// dash, and a process whose server never came up reports <c>mode=failed</c>.
/// </summary>
internal static class StartupTimeline
{
    private static readonly Stopwatch Clock = new();
    private static readonly Lock Gate = new();
    private static double? _serverReady;
    private static double? _uiReady;
    private static double? _desktopReady;
    private static string _mode = "failed";
    private static bool _reported;

    /// <summary>Anchors the clock; the process's first statement.</summary>
    public static void Arm() => Clock.Start();

    /// <summary>An installed Host answered: the window uses the server that is already running.</summary>
    public static void HostReused()
    {
        lock (Gate)
        {
            _mode = "shared";
            _serverReady ??= Elapsed;
        }
    }

    /// <summary>The embedded server answered with the address it serves the UI on.</summary>
    public static void ServerStarted()
    {
        lock (Gate)
        {
            _mode = "embedded";
            _serverReady ??= Elapsed;
        }
    }

    /// <summary>Nothing serves the UI: the window opens on the reason instead.</summary>
    public static void ServerUnavailable()
    {
        lock (Gate)
        {
            _mode = "failed";
        }
    }

    /// <summary>The window exists and is the application's main window.</summary>
    public static void UiReady()
    {
        lock (Gate)
        {
            _uiReady ??= Elapsed;
        }
    }

    /// <summary>The web view finished its first navigation: the UI is on screen.</summary>
    public static void UiLoaded()
    {
        lock (Gate)
        {
            _desktopReady ??= Elapsed;
        }

        Report();
    }

    /// <summary>Writes the marker line once; later calls change nothing.</summary>
    public static void Report()
    {
        lock (Gate)
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            try
            {
                Console.Out.WriteLine(
                    $"Startup: desktop ready mode={_mode}\t{Milliseconds(_serverReady)}\t{Milliseconds(_uiReady)}\t{Milliseconds(_desktopReady)}");
                Console.Out.Flush();
            }
            catch (IOException)
            {
                // A windowless launch may have no usable standard output: the marker is
                // diagnostics and never a reason for the start to fail.
            }
            catch (ObjectDisposedException)
            {
                // The harness closed the pipe first; the run is over anyway.
            }
        }
    }

    private static double Elapsed => Clock.Elapsed.TotalMilliseconds;

    private static string Milliseconds(double? value) =>
        value?.ToString("F0", CultureInfo.InvariantCulture) ?? "-";
}
