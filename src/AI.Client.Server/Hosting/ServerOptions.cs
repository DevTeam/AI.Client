namespace AI.Client.Server.Hosting;

/// <summary>What an entry point decided about the server before starting it.</summary>
/// <param name="DataDirectory">Absolute root for projects, chats, settings and logs.</param>
/// <param name="Urls">
/// Addresses to listen on, separated by semicolons, or null to leave the choice to the ASP.NET
/// defaults (<c>ASPNETCORE_URLS</c>, launch settings).
/// </param>
/// <param name="BrowseEnabled">
/// Whether the host exposes its local file system browser to the UI. See
/// <see cref="Endpoints.FileSystemEndpoints"/> for why a deployment might turn it off.
/// </param>
public sealed record ServerOptions(string DataDirectory, string? Urls, bool BrowseEnabled);
