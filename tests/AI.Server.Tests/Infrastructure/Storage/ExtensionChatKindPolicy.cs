namespace AI.Infrastructure.Tests.Storage;

using System.Text.Json;
using AI.Application.Chats;
using AI.Contracts.Chats;
using AI.Domain.Chats;

/// <summary>A test-only kind that proves the shipped execution graph accepts another policy.</summary>
internal sealed class ExtensionChatKindPolicy : IChatKindPolicy
{
    public static readonly ChatKind KindValue = new("fixture");

    public ChatKind Kind => KindValue;
    public ChatKindBehavior Behavior { get; } = new(Persistence: ChatPersistence.HostLifetime);

    public void ValidateState(JsonElement? state, int version)
    {
        if (version != 3 || state is null || !state.Value.TryGetProperty("marker", out _))
            throw new ArgumentException("Fixture state is invalid.");
    }

    public Task<ChatDetails> InitializeAsync(ChatDetails chat, IChatService chats, CancellationToken token) =>
        Task.FromResult(chat);
    public string? NavigationMode(JsonElement? state) => null;
    public bool AllowsServer(Guid serverId) => true;
    public bool AllowsTool(Guid serverId, string toolName) => true;
    public Task<bool> ShouldCleanUpAsync(StoredChatSummary summary,
        Func<CancellationToken, Task<ChatDetails?>> loadChat, CancellationToken token) => Task.FromResult(false);
    public Task OnHostStartedAsync(CancellationToken token) => Task.CompletedTask;
    public Task OnHostStoppingAsync(CancellationToken token) => Task.CompletedTask;
}
