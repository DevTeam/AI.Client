using AI.Client.Application.Chats;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;
using System.Text.Json;

namespace AI.Client.Infrastructure.Storage;

public sealed class ChatDocumentSerializer : IChatDocumentSerializer
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Serialize(ChatThread chat, long revision) => JsonSerializer.Serialize(new ChatDocument(
        SchemaVersion,
        revision,
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.CreatedAt,
        chat.UpdatedAt,
        chat.EndpointProfileId?.Value,
        chat.Messages.Select(message => new ChatMessageDocument(
            message.Id.Value,
            message.ParentId?.Value,
            message.Role,
            message.Content,
            message.CreatedAt,
            message.IsIncomplete)).ToArray(),
        chat.BranchTitles.ToDictionary(item => item.Key.Value, item => item.Value)), Options);

    public StoredChat Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<ChatDocument>(json, Options)
            ?? throw new JsonException("Chat document is empty.");
        if (document.SchemaVersion != SchemaVersion || document.Revision < 0)
        {
            throw new JsonException("Chat document schema or revision is invalid.");
        }

        var chat = new ChatThread(
            new ChatId(document.Id),
            new ProjectId(document.ProjectId),
            document.Title,
            document.CreatedAt,
            document.EndpointProfileId is { } endpointId ? new EndpointProfileId(endpointId) : null);
        foreach (var message in document.Messages.OrderBy(item => item.CreatedAt))
        {
            chat.AddMessage(new ChatMessage(
                new ChatMessageId(message.Id),
                message.ParentId is { } parentId ? new ChatMessageId(parentId) : null,
                message.Role,
                message.Content,
                message.CreatedAt,
                message.IsIncomplete), message.CreatedAt);
        }
        foreach (var branch in document.BranchTitles ?? [])
        {
            chat.RenameBranch(new ChatMessageId(branch.Key), branch.Value, chat.UpdatedAt);
        }

        return new StoredChat(chat, document.Revision);
    }

    private sealed record ChatDocument(
        int SchemaVersion,
        long Revision,
        Guid Id,
        Guid ProjectId,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? EndpointProfileId,
        ChatMessageDocument[] Messages,
        Dictionary<Guid, string>? BranchTitles = null);

    private sealed record ChatMessageDocument(
        Guid Id,
        Guid? ParentId,
        ChatMessageRole Role,
        string Content,
        DateTimeOffset CreatedAt,
        bool IsIncomplete = false);
}
