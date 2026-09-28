namespace AI.Web.Settings;

using AI.Contracts.Settings;

/// <summary>
/// Settings as text to copy, paste and save to a file. MCP servers use the <c>mcpServers</c> shape
/// of Claude Desktop, Cursor and Claude Code, so a snippet from a server's README pastes as is and
/// an export pastes into those clients. Keys and secret values never go out and never come in.
/// </summary>
public interface ISettingsTransferCodec
{
    string Export(IReadOnlyList<ConnectionSettings> connections, IReadOnlyList<McpServerSettings> mcpServers);

    SettingsTransferParseResult Parse(string text);
}
