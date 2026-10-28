namespace AI.Application.Tools;

using System.Text.Json;

/// <summary>
/// The last <c>tools/list</c> answer of each external MCP server, kept across turns and restarts.
/// With it a turn can offer a server's tools without starting the server, which is then started
/// only when one of its tools is called.
/// </summary>
public interface IMcpToolListCache
{
    /// <summary>
    /// The server's tools as raw protocol descriptors, or null when none were stored for this
    /// configuration. <paramref name="fingerprint"/> identifies the configuration: a list stored
    /// for another command, environment or credential is not the list of this one.
    /// </summary>
    Task<IReadOnlyList<JsonElement>?> GetAsync(Guid serverId, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Stores the server's tools for <paramref name="fingerprint"/>, replacing whatever it had.</summary>
    Task SetAsync(Guid serverId, string fingerprint, IReadOnlyList<JsonElement> tools, CancellationToken cancellationToken);
}
