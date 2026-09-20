namespace AI.Client.Mcp.App;

using System.Text.Json;

/// <summary>
/// One page of application data. Every resource answers in the same shape so a single tool can
/// serve lists and single documents alike: a document is simply a page of one item.
/// </summary>
/// <param name="Resource">Echo of the requested resource, so a result read out of history stands alone.</param>
/// <param name="Items">The documents themselves, already shaped by the application's own contracts.</param>
/// <param name="NextCursor">Opaque cursor for the next page, or null when the page is the last one.</param>
/// <param name="Truncated">True when the character budget, not the requested limit, ended the page.</param>
public sealed record AppReadResult(
    string Resource,
    IReadOnlyList<JsonElement> Items,
    string? NextCursor,
    bool Truncated,
    int Returned,
    int Total,
    string? Error);

/// <summary>
/// The outcome of one mutation. A dry run reports what would happen and changes nothing; a
/// revision conflict reports the current revision and the current document, so the caller can
/// decide for itself whether to retry rather than having the Host retry behind its back.
/// </summary>
public sealed record AppWriteResult(
    string Operation,
    bool Applied,
    bool DryRun,
    string Effect,
    Guid? ProjectId,
    Guid? ChatId,
    Guid? BranchId,
    Guid? MessageId,
    long Revision,
    string? Status,
    JsonElement? Current,
    bool Replayed,
    string? Error);

public sealed record DirectoryGrantPayload(
    Guid Id,
    string DisplayName,
    string CanonicalRoot,
    bool Recursive,
    IReadOnlyList<string> ToolNames);

public sealed record McpServerBindingPayload(Guid Id, string DisplayName, string Transport, bool Enabled);

public sealed record ToolPolicyPayload(
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Decision,
    int MaxCallsPerRun,
    long TimeoutSeconds);

public sealed record ProjectSecurityPayload(
    IReadOnlyList<DirectoryGrantPayload> DirectoryGrants,
    IReadOnlyList<McpServerBindingPayload> McpServers,
    IReadOnlyList<ToolPolicyPayload> ToolPolicies);

public sealed record ConnectionPayload(
    Guid Id,
    string Name,
    string BaseUrl,
    string Model,
    bool Enabled,
    bool IsDefault,
    bool ForSubtasks = false,
    int? Capability = null,
    int? Cost = null,
    string? GoodFor = null,
    long? ContextWindowTokens = null,
    long? ReservedOutputTokens = null);

public sealed record McpEnvironmentVariablePayload(string Name, string? Value, bool IsSecret);

public sealed record McpServerPayload(
    Guid Id,
    string Name,
    string Transport,
    bool Enabled,
    string Policy,
    string? Url,
    string? Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyList<McpEnvironmentVariablePayload> EnvironmentVariables);

public sealed record GlobalSettingsPayload(
    IReadOnlyList<ConnectionPayload> Connections,
    IReadOnlyList<McpServerPayload> McpServers,
    IReadOnlyList<ToolPolicyPayload> ToolPolicies);
