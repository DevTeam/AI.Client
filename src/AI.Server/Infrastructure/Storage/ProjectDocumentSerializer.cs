using AI.Application.Projects;
using AI.Contracts.Settings;
using AI.Domain.Projects;
using AI.Infrastructure.Settings;
using System.Text.Json;
// ReSharper disable UseCollectionExpression

namespace AI.Infrastructure.Storage;

public sealed class ProjectDocumentSerializer : IProjectDocumentSerializer
{
    private const int SchemaVersion = 2;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Serialize(Project project, long revision)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);

        return JsonSerializer.Serialize(new ProjectDocument(
            SchemaVersion,
            revision,
            project.Id.Value,
            project.Name,
            project.Description,
            project.CreatedAt,
            project.UpdatedAt,
            project.DirectoryGrants.Select(grant => new DirectoryGrantDocument(
                grant.Id.Value,
                grant.DisplayName,
                grant.CanonicalRoot,
                grant.Recursive,
                grant.ToolNames.Order(StringComparer.Ordinal).ToArray())).ToArray(),
            project.McpServers.Select(server => new McpServerDocument(
                server.Id.Value,
                server.DisplayName,
                server.Transport,
                server.Enabled)).ToArray(),
            project.ToolPolicies.Select(policy => new ToolPolicyDocument(
                policy.Tool.ServerId.Value,
                policy.Tool.Name,
                policy.Tool.SchemaHash,
                policy.Decision,
                policy.MaxCallsPerRun,
                policy.Timeout?.Ticks)).ToArray(),
            project.ConnectionId?.Value),
            Options);
    }

    public StoredProject Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, Options)
            ?? throw new JsonException("Project document is empty.");
        if (document.SchemaVersion != SchemaVersion)
        {
            throw new JsonException($"Unsupported project schema version '{document.SchemaVersion}'.");
        }

        if (document.Revision < 0)
        {
            throw new JsonException("Project revision cannot be negative.");
        }

        var project = new Project(
            new ProjectId(document.Id),
            document.Name,
            document.Description,
            document.CreatedAt);
        if (document.UpdatedAt != document.CreatedAt)
        {
            project.UpdateDetails(document.Name, document.Description, document.UpdatedAt);
        }

        foreach (var grant in document.DirectoryGrants)
        {
            project.AddDirectoryGrant(
                new DirectoryGrant(
                    new DirectoryGrantId(grant.Id),
                    grant.DisplayName,
                    grant.CanonicalRoot,
                    grant.Recursive,
                    grant.ToolNames),
                document.UpdatedAt);
        }

        // The retired C# scripting server's binding goes; whether it was switched off for this
        // project decides whether its cs_run policies move to the built-in server as Deny.
        var legacyCSharp = document.McpServers.SingleOrDefault(server => server.Id == LegacyCSharpMcpServer.Id);
        foreach (var server in document.McpServers.Where(server => server.Id != LegacyCSharpMcpServer.Id))
        {
            project.AddMcpServer(
                new McpServerBinding(
                    new McpServerId(server.Id),
                    server.DisplayName,
                    server.Transport,
                    server.Enabled),
                document.UpdatedAt);
        }

        var policies = legacyCSharp is not null || document.ToolPolicies.Any(policy => policy.ServerId == LegacyCSharpMcpServer.Id)
            ? LegacyCSharpToolMigration.Migrate(document.ToolPolicies, legacyCSharp is { Enabled: false },
                policy => (policy.ServerId, policy.Name, policy.SchemaHash),
                (policy, deny) => policy with
                {
                    ServerId = DefaultMcpServer.Id, Decision = deny ? ToolPolicyDecision.Deny : policy.Decision
                },
                schemaHash => new ToolPolicyDocument(DefaultMcpServer.Id, LegacyCSharpMcpServer.ScriptToolName,
                    schemaHash, ToolPolicyDecision.Deny, null, null)).ToArray()
            : document.ToolPolicies;
        // A project policy needs its server bound to the project, so moved policies bring the
        // built-in server's binding along, switched on like the one the security editor adds.
        if (policies.Any(policy => policy.ServerId == DefaultMcpServer.Id)
            && project.McpServers.All(server => server.Id.Value != DefaultMcpServer.Id))
            project.AddMcpServer(new McpServerBinding(new McpServerId(DefaultMcpServer.Id),
                DefaultMcpServer.Settings.Name, McpTransportKind.Stdio, true), document.UpdatedAt);
        foreach (var policy in policies)
        {
            project.SetToolPolicy(
                new ToolPolicy(
                    new ToolIdentity(new McpServerId(policy.ServerId), policy.Name, policy.SchemaHash),
                    policy.Decision,
                    policy.MaxCallsPerRun,
                    policy.TimeoutTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null),
                document.UpdatedAt);
        }

        project.SetConnection(
            document.ConnectionId is { } connectionId ? new ConnectionId(connectionId) : null,
            document.UpdatedAt);

        return new StoredProject(project, document.Revision);
    }

    private sealed record ProjectDocument(
        // ReSharper disable once MemberHidesStaticFromOuterClass
        int SchemaVersion,
        long Revision,
        Guid Id,
        string Name,
        string Description,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DirectoryGrantDocument[] DirectoryGrants,
        McpServerDocument[] McpServers,
        ToolPolicyDocument[] ToolPolicies,
        Guid? ConnectionId = null);

    private sealed record DirectoryGrantDocument(
        Guid Id,
        string DisplayName,
        string CanonicalRoot,
        bool Recursive,
        string[] ToolNames);

    private sealed record McpServerDocument(Guid Id, string DisplayName, McpTransportKind Transport, bool Enabled);

    private sealed record ToolPolicyDocument(
        Guid ServerId,
        string Name,
        string SchemaHash,
        ToolPolicyDecision Decision,
        int? MaxCallsPerRun,
        long? TimeoutTicks);

}
