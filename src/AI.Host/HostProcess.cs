namespace AI.Host;

using System.Diagnostics;

internal sealed class HostProcess : IHostProcess
{
    public void StartInBackground()
    {
        // The same executable in Host mode. If the service manager starts another one later, it
        // waits for this one to release the data directory instead of opening a second store.
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("The Host executable path is unknown."))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add("--public-web");
        using var _ = Process.Start(start);
    }
}
