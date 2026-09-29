namespace AI.Host;

/// <summary>The icon in the notification area (the menu bar on macOS) of a background Host.</summary>
internal interface IHostTray
{
    /// <summary>
    /// False where no icon can be shown at all, such as a Linux session without a display; the
    /// Host then runs without one, as a plain server.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Shows the icon on the calling thread, which must be the process's main one, until
    /// <paramref name="server"/> completes. Quitting from the icon cancels <paramref name="stop"/>.
    /// </summary>
    void Run(Task<int> server, CancellationTokenSource stop, string dataDirectory);
}
