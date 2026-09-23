namespace AI.Client.Server.Hosting;

/// <summary>What an entry point decided about the server before starting it.</summary>
/// <param name="DataDirectory">Absolute root for projects, chats, settings and logs.</param>
/// <param name="Urls">
/// Addresses to listen on, separated by semicolons, or null to leave the choice to the ASP.NET
/// defaults (<c>ASPNETCORE_URLS</c>, launch settings). Port 0 picks a free port; the one actually
/// bound is reported by <see cref="IRunningServer.Address"/>.
/// </param>
/// <param name="BrowseEnabled">
/// Whether the host exposes its local file system browser to the UI. See
/// <see cref="Endpoints.FileSystemEndpoints"/> for why a deployment might turn it off.
/// </param>
/// <param name="ServeWeb">
/// Whether the server also serves the Blazor UI, so that the UI and the API share one origin.
/// The desktop app always does; the standalone host does when asked, and otherwise leaves the
/// UI to its own dev server.
/// </param>
public sealed record ServerOptions(string DataDirectory, string? Urls, bool BrowseEnabled, bool ServeWeb = false);
