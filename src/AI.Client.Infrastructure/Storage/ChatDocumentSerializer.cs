using AI.Client.Application.Chats;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;
using System.Text.Json;
// ReSharper disable UseCollectionExpression

namespace AI.Client.Infrastructure.Storage;

public static class ChatDocumentSerializer
{
    private const int SchemaVersion = 4;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(ChatThread chat, long revision) => JsonSerializer.Serialize(new ChatDocument(
        SchemaVersion,
        revision,
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.CreatedAt,
        chat.UpdatedAt,
        chat.ConnectionId?.Value,
        chat.Messages.Select(message => new ChatMessageDocument(
            message.Id.Value,
            message.ParentId?.Value,
            message.Role,
            message.Content,
            message.CreatedAt,
            message.IsIncomplete, message.ToolCalls, message.ToolCallId)).ToArray(),
        chat.Branches.Select(branch => new BranchDocument(branch.Id, branch.HeadMessageId?.Value, branch.Title,
            branch.ParentBranchId, branch.RootMessageId?.Value, branch.Revision)).ToArray(),
        chat.ToolPolicies.Select(policy => new ToolPolicyDocument(policy.Tool.ServerId.Value, policy.Tool.Name,
            policy.Tool.SchemaHash, policy.Decision, policy.MaxCallsPerRun, policy.Timeout)).ToArray(),
        chat.IsPinned,
        chat.PinnedAt,
        chat.LastActivityAt), Options);

    public static StoredChat Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<ChatDocument>(json, Options)
            ?? throw new JsonException("Chat document is empty.");
        if (document.SchemaVersion is not (3 or SchemaVersion) || document.Revision < 0)
        {
            throw new JsonException("Chat document schema or revision is invalid.");
        }

        var chat = new ChatThread(
            new ChatId(document.Id),
            new ProjectId(document.ProjectId),
            document.Title,
            document.CreatedAt,
            document.ConnectionId is { } endpointId ? new ConnectionId(endpointId) : null);
        foreach (var message in document.Messages.OrderBy(item => item.CreatedAt))
        {
            chat.AddMessage(new ChatMessage(
                new ChatMessageId(message.Id),
                message.ParentId is { } parentId ? new ChatMessageId(parentId) : null,
                message.Role,
                message.Content,
                message.CreatedAt,
                message.IsIncomplete, message.ToolCalls, message.ToolCallId), message.CreatedAt);
        }
        chat.RestoreBranches(document.Branches.Select(branch => new ChatBranch(branch.Id,
            branch.HeadMessageId is { } head ? new ChatMessageId(head) : null, branch.Title,
            branch.ParentBranchId, branch.RootMessageId is { } root ? new ChatMessageId(root) : null, branch.Revision)));
        foreach (var policy in document.ToolPolicies ?? [])
            chat.SetToolPolicy(new ToolPolicy(new ToolIdentity(new McpServerId(policy.ServerId), policy.Name,
                policy.SchemaHash), policy.Decision, policy.MaxCallsPerRun, policy.Timeout), document.UpdatedAt);
        chat.Rename(document.Title, document.UpdatedAt);
        chat.RestorePinState(document.IsPinned, document.PinnedAt);

        return new StoredChat(chat, document.Revision);
    }

    private sealed record ChatDocument(
        // ReSharper disable once MemberHidesStaticFromOuterClass
        int SchemaVersion,
        long Revision,
        Guid Id,
        Guid ProjectId,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? ConnectionId,
        ChatMessageDocument[] Messages,
        BranchDocument[] Branches,
        ToolPolicyDocument[]? ToolPolicies = null,
        bool IsPinned = false,
        DateTimeOffset? PinnedAt = null,
        DateTimeOffset LastActivityAt = default);

    private sealed record ToolPolicyDocument(Guid ServerId, string Name, string SchemaHash,
        ToolPolicyDecision Decision, int MaxCallsPerRun, TimeSpan Timeout);

    private sealed record BranchDocument(Guid Id, Guid? HeadMessageId, string Title, Guid? ParentBranchId,
        Guid? RootMessageId, long Revision);

    private sealed record ChatMessageDocument(
        Guid Id,
        Guid? ParentId,
        ChatMessageRole Role,
        string Content,
        DateTimeOffset CreatedAt,
        bool IsIncomplete = false,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ChatToolCall>? ToolCalls = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? ToolCallId = null);
}
