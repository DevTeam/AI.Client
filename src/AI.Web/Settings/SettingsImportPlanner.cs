namespace AI.Web.Settings;

using System.Text.Json;
using AI.Contracts.Settings;

public sealed class SettingsImportPlanner : ISettingsImportPlanner
{
    private const string ApiKey = "API key";
    private const string Credential = "Credential";

    public SettingsImportPlan Plan(
        SettingsTransferParseResult parsed,
        IReadOnlyList<ConnectionSettings> connections,
        IReadOnlyList<McpServerSettings> mcpServers) => new(
        [.. parsed.Connections.Select(item => PlanConnection(item, connections))],
        [.. parsed.McpServers.Select(item => PlanMcpServer(item, mcpServers))],
        parsed.Skipped,
        parsed.SecretValuesDropped);

    public string UniqueName(string name, IEnumerable<string> taken)
    {
        var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var index = 2; names.Contains(candidate); index++) candidate = $"{name} ({index})";
        return candidate;
    }

    private static SettingsImportCandidate<ConnectionSettings> PlanConnection(
        ImportedSettingsItem<ConnectionSettings> imported,
        IReadOnlyList<ConnectionSettings> current)
    {
        var addAs = imported.Settings;
        IReadOnlyList<string> missingIfAdded = imported.CredentialOmitted ? [ApiKey] : [];
        var existing = current.FirstOrDefault(item => SameName(item.Name, addAs.Name));
        if (existing is null)
            return new(imported, addAs, null, null, SettingsImportMatch.New, missingIfAdded, [], false, false);

        // Which connection is the default, and which take subtasks, is a choice made on this Host
        // about its own set; an import describes an endpoint, not that choice.
        var replaceWith = addAs with
        {
            Id = existing.Id,
            Name = existing.Name,
            IsDefault = existing.IsDefault && addAs.Enabled,
            ForSubtasks = existing.ForSubtasks && addAs.Enabled,
            HasCredential = existing.HasCredential
        };
        IReadOnlyList<string> missingIfReplaced = imported.CredentialOmitted && !existing.HasCredential ? [ApiKey] : [];
        return new(imported, addAs, existing, replaceWith, Match(existing, replaceWith),
            missingIfAdded, missingIfReplaced, false, false);
    }

    private static SettingsImportCandidate<McpServerSettings> PlanMcpServer(
        ImportedSettingsItem<McpServerSettings> imported,
        IReadOnlyList<McpServerSettings> current)
    {
        var settings = imported.Settings;
        // A command line from someone else's text is a program this Host would start. It comes in
        // switched off, so it runs only once somebody has looked at it here.
        var switchedOffIfAdded = settings.Transport == "Stdio" && settings.Enabled;
        var addAs = switchedOffIfAdded ? settings with { Enabled = false } : settings;
        var missingIfAdded = Missing(imported, null);
        var existing = current.FirstOrDefault(item => SameName(item.Name, settings.Name));
        if (existing is null)
            return new(imported, addAs, null, null, SettingsImportMatch.New, missingIfAdded, [], switchedOffIfAdded, false);

        if (existing.Id == DefaultMcpServer.Id || existing.Id == AppMcpServer.Id)
            return new(imported, addAs, existing, null, SettingsImportMatch.Conflict, missingIfAdded, [], switchedOffIfAdded, false);

        // The same program as before keeps the switch where the user left it; a different one is
        // as unreviewed as a new server.
        var sameProcess = settings.Transport == existing.Transport
            && string.Equals(settings.Command, existing.Command, StringComparison.Ordinal)
            && settings.Arguments.SequenceEqual(existing.Arguments, StringComparer.Ordinal);
        var stdio = settings.Transport == "Stdio";
        var enabled = stdio ? sameProcess && existing.Enabled : settings.Enabled;
        var savedSecrets = SavedSecrets(existing);
        var replaceWith = settings with
        {
            Id = existing.Id,
            Name = existing.Name,
            Enabled = enabled,
            HasCredential = existing.HasCredential,
            EnvironmentVariables = [.. settings.EnvironmentVariables.Select(variable => variable.IsSecret
                ? variable with { HasSecret = savedSecrets.Contains(variable.Name) }
                : variable)]
        };
        return new(imported, addAs, existing, replaceWith, Match(WithoutSecretValues(existing), WithoutSecretValues(replaceWith)),
            missingIfAdded, Missing(imported, existing), switchedOffIfAdded, stdio && !sameProcess && existing.Enabled);
    }

    private static List<string> Missing(ImportedSettingsItem<McpServerSettings> imported, McpServerSettings? existing)
    {
        var saved = existing is null ? [] : SavedSecrets(existing);
        var missing = imported.Settings.EnvironmentVariables
            .Where(variable => variable.IsSecret && !saved.Contains(variable.Name))
            .Select(variable => variable.Name).ToList();
        if (imported.CredentialOmitted && existing is not { HasCredential: true }) missing.Add(Credential);
        return missing;
    }

    private static HashSet<string> SavedSecrets(McpServerSettings server) =>
        server.EnvironmentVariables.Where(variable => variable.IsSecret && variable.HasSecret)
            .Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);

    // The editor sends a secret it has not been given as an empty value, an import as none.
    private static McpServerSettings WithoutSecretValues(McpServerSettings server) => server with
    {
        EnvironmentVariables = [.. server.EnvironmentVariables.Select(variable => variable.IsSecret ? variable with { Value = null } : variable)]
    };

    private static bool SameName(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    // Records compare their lists by reference; what matters here is what they would save.
    private static SettingsImportMatch Match<T>(T existing, T replaceWith) =>
        JsonSerializer.Serialize(existing) == JsonSerializer.Serialize(replaceWith)
            ? SettingsImportMatch.Same
            : SettingsImportMatch.Conflict;
}
