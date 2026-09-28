namespace AI.Web;

/// <summary>
/// The published Web app's link to the local Host: whether it answers right now, what it is, and
/// how this browser gives up its grant. Only meaningful when <see cref="IClientMode.PublicWeb"/>.
/// </summary>
public interface IHostConnection
{
    HostConnectionStatus Status { get; }

    /// <summary>What the Host reported about itself, or null until it has answered once.</summary>
    HostSession? Session { get; }

    Uri Address { get; }

    event Action? Changed;

    /// <summary>Called by the run event stream, which is the one request that is always open.</summary>
    Task ReportAsync(bool connected);

    /// <summary>Revokes this browser's grant and returns to the connection page; false if the Host did not answer.</summary>
    Task<bool> DisconnectAsync();
}

public enum HostConnectionStatus
{
    Connecting,
    Connected,
    Offline
}

public sealed record HostSession(string ProductName, string Version, int ApiVersion, bool DesktopInstalled);
