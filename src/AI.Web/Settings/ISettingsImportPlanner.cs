namespace AI.Web.Settings;

using AI.Contracts.Settings;

/// <summary>
/// Matches imported items to the current ones by name and works out what each choice would do.
/// </summary>
public interface ISettingsImportPlanner
{
    SettingsImportPlan Plan(
        SettingsTransferParseResult parsed,
        IReadOnlyList<ConnectionSettings> connections,
        IReadOnlyList<McpServerSettings> mcpServers);

    /// <summary><paramref name="name"/>, or "name (2)", "name (3)" … when it is taken.</summary>
    string UniqueName(string name, IEnumerable<string> taken);
}
