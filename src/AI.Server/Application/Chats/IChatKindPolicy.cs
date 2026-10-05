namespace AI.Application.Chats;

using System.Text.Json;
using AI.Domain.Chats;

public enum ChatPersistence { Durable, HostLifetime }

/// <summary>Decisions the common chat pipeline needs for one run and its presentation.</summary>
public sealed record ChatKindBehavior(
    ChatPersistence Persistence = ChatPersistence.Durable,
    bool ShowInChatList = true,
    bool ShowInMainRuns = true,
    bool AllowChatNavigation = true,
    string InteractionSurface = "chat",
    bool UseFullHistory = false,
    bool SuggestReplies = true,
    bool IncludeStandingInstructions = true,
    bool RouteSkills = true,
    bool UseActiveSkills = true,
    bool AllowDirectoryGrants = true,
    bool ForceOverlayQuestions = false,
    bool AllowUntimedQuestions = false,
    bool RestrictNavigation = false,
    bool CanCreateDemo = false,
    bool ContinueOnUnavailableNavigation = false,
    string? FinishingInstruction = null,
    string? CompactFinishingInstruction = null,
    IReadOnlyList<string>? PinnedTools = null);

/// <summary>One registration owns all behavior specific to a chat kind.</summary>
public interface IChatKindPolicy
{
    ChatKind Kind { get; }
    ChatKindBehavior Behavior { get; }
    void ValidateState(JsonElement? state, int version);
    Task<AI.Contracts.Chats.ChatDetails> InitializeAsync(AI.Contracts.Chats.ChatDetails chat, IChatService chats,
        CancellationToken token);
    string? NavigationMode(JsonElement? state);
    bool AllowsServer(Guid serverId);
    bool AllowsTool(Guid serverId, string toolName);
    Task<bool> ShouldCleanUpAsync(StoredChatSummary summary,
        Func<CancellationToken, Task<AI.Contracts.Chats.ChatDetails?>> loadChat, CancellationToken token);
    Task OnHostStartedAsync(CancellationToken token);
    Task OnHostStoppingAsync(CancellationToken token);
}

public interface IChatKindPolicyRegistry
{
    IChatKindPolicy Resolve(ChatKind kind);
    IChatKindPolicy? TryResolve(ChatKind kind);
    IEnumerable<IChatKindPolicy> All { get; }
}

public sealed class ChatKindPolicyRegistry : IChatKindPolicyRegistry
{
    private readonly IReadOnlyDictionary<ChatKind, IChatKindPolicy> _policies;

    public ChatKindPolicyRegistry(IEnumerable<IChatKindPolicy> policies)
    {
        var registered = policies.ToArray();
        var duplicate = registered.GroupBy(policy => policy.Kind).FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate chat kind '{duplicate.Key}'.");
        _policies = registered.ToDictionary(policy => policy.Kind);
    }

    public IChatKindPolicy Resolve(ChatKind kind) => _policies.TryGetValue(Normalize(kind), out var policy)
        ? policy : throw new InvalidOperationException($"Chat kind '{kind}' is not registered.");

    public IChatKindPolicy? TryResolve(ChatKind kind) => _policies.GetValueOrDefault(Normalize(kind));

    private static ChatKind Normalize(ChatKind kind) => kind == default ? ChatKind.Conversation : kind;

    public IEnumerable<IChatKindPolicy> All => _policies.Values;
}
