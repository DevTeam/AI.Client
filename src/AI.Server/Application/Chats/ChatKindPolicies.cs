namespace AI.Application.Chats;

using System.Text.Json;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Settings;
using AI.Domain.Chats;

public sealed class ConversationChatKindPolicy : IChatKindPolicy
{
    public ChatKind Kind => ChatKind.Conversation;
    public ChatKindBehavior Behavior { get; } = new();
    public void ValidateState(JsonElement? state, int version)
    {
        RequireVersion(version);
        RequireEmpty(state);
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

    internal static void RequireEmpty(JsonElement? state)
    {
        if (state is not null && state.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            throw new ArgumentException("This chat kind does not accept state.");
    }

    internal static void RequireVersion(int version)
    {
        if (version != 1) throw new ArgumentException("Unsupported chat kind state version.");
    }
}

// Projects arrive as a factory: Pure.DI 2.5.4 otherwise creates the project repository singleton
// only inside the lazy enumeration of kind policies and hands the run dispatcher a ProjectService
// over a null repository.
public sealed class GuideChatKindPolicy(Func<IProjectService> projects, Func<IGuideChats> guideChats) : IChatKindPolicy
{
    public ChatKind Kind => ChatKind.Guide;
    public ChatKindBehavior Behavior { get; } = new(
        ShowInChatList: false, ShowInMainRuns: false, AllowChatNavigation: false,
        InteractionSurface: "guide", UseFullHistory: true, SuggestReplies: false,
        IncludeStandingInstructions: false, RouteSkills: false, UseActiveSkills: false,
        AllowDirectoryGrants: false, ForceOverlayQuestions: true, AllowUntimedQuestions: true,
        RestrictNavigation: true, CanCreateDemo: true, ContinueOnUnavailableNavigation: true,
        FinishingInstruction: GuideFinishingInstruction,
        PinnedTools: ["app_navigate", "ask_user"]);

    public void ValidateState(JsonElement? state, int version)
    {
        ConversationChatKindPolicy.RequireVersion(version);
        if (state is null) return;
        if (state.Value.ValueKind != JsonValueKind.Object || !state.Value.TryGetProperty("mode", out var mode)
            || mode.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(mode.GetString()))
            throw new ArgumentException("Guide state requires a mode.");
    }
    public Task<ChatDetails> InitializeAsync(ChatDetails chat, IChatService chats, CancellationToken token) =>
        Task.FromResult(chat);

    public string? NavigationMode(JsonElement? state) =>
        state is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("mode", out var mode)
            ? mode.GetString() : "show";

    public bool AllowsServer(Guid serverId) => serverId == AppMcpServer.Id;
    public bool AllowsTool(Guid serverId, string toolName) =>
        serverId == AppMcpServer.Id && toolName is "app_navigate" or "ask_user";
    public Task<bool> ShouldCleanUpAsync(StoredChatSummary summary,
        Func<CancellationToken, Task<ChatDetails?>> loadChat, CancellationToken token) => Task.FromResult(true);

    public async Task OnHostStartedAsync(CancellationToken token)
    {
        foreach (var project in await projects().ListAsync(token))
            await guideChats().CleanUpAsync(project.Id, null, token);
    }

    public Task OnHostStoppingAsync(CancellationToken token) => Task.CompletedTask;

    private const string GuideFinishingInstruction =
        "This is a visual application guide. Teach with app_navigate comments beside the relevant controls; "
        + "all substantive explanations, examples and takeaways belong there. Ask learning choices through ask_user, "
        + "with concrete options, allowOther=true and an invitation to write a custom interest. Follow the answers. "
        + "Opening interests and questions within an unfinished topic explicitly use timeoutSeconds=0 and timeoutBehavior='cancel'. "
        + "Recommend continuing that topic, not Stop. Only an important fork after the current branch and takeaway are complete "
        + "may use timeoutSeconds=30 and timeoutBehavior='cancel', with Finish recommended; silence then ends the completed tour. "
        + "Do not infer topic completion from a fixed number of steps. Do not advance or choose while a question is pending. "
        + "Assistant chat content may only record brief progress markers, never essays or instructional lists. "
        + "Use aiclient://navigate Markdown links in visible step comments and ask_user question text for useful optional routes; "
        + "include a relevant shortcut in the final visible takeaway. Hidden service-chat messages are not visible, so put links in the visible UI. "
        + "Links reveal destinations only: a click is not Continue, an answer or permission to modify anything. Keep using app_navigate for timed tour steps. "
        + "Include the next tool call with progress text: a reply without a tool call ends the tour. "
        + "Do not create or change content as a demonstration. Stop when a navigation result is stopped/expired. "
        + "On unavailable, discover targets and offer a visible route through ask_user instead of ending the guide. "
        + "Use app_navigate action='show' on widgets.* even when not currently visible: the window temporarily shows, opens and scrolls to it. "
        + "Stop when an outside-chat question is cancelled or unanswered. Use the tour's resolved language. Finish with one short "
        + "completion or stop phrase; do not recap the lesson at length in chat.";
}

public sealed class DemoChatKindPolicy : IChatKindPolicy
{
    public const string Title = "Guide demo";
    private const string Question = "Suggest a name for a small weather app.";
    private const string Answer = "Three ideas:\n\n1. **Skyline**: short and calm.\n2. **Drizzle**: light and playful.\n"
        + "3. **Forecastly**: says what it does.\n\nThis chat was set up by the application guide to show forking, branches and "
        + "comments. It is removed when the tour ends unless you write in it.";
    private const int InitialMessages = 2;

    public ChatKind Kind => ChatKind.Demo;
    public ChatKindBehavior Behavior { get; } = new();
    public void ValidateState(JsonElement? state, int version)
    {
        ConversationChatKindPolicy.RequireVersion(version);
        ConversationChatKindPolicy.RequireEmpty(state);
    }
    public async Task<ChatDetails> InitializeAsync(ChatDetails chat, IChatService chats, CancellationToken token)
    {
        var questionId = Guid.CreateVersion7();
        chat = await chats.AppendMessageAsync(chat.ProjectId, chat.Id,
                   new AppendChatMessageRequest(questionId, null, "User", Question, chat.Revision), token)
               ?? throw new InvalidOperationException("The demo chat could not be written.");
        return await chats.AppendMessageAsync(chat.ProjectId, chat.Id,
                   new AppendChatMessageRequest(Guid.CreateVersion7(), questionId, "Assistant", Answer, chat.Revision), token)
               ?? throw new InvalidOperationException("The demo chat could not be written.");
    }
    public string? NavigationMode(JsonElement? state) => null;
    public bool AllowsServer(Guid serverId) => true;
    public bool AllowsTool(Guid serverId, string toolName) => true;

    public async Task<bool> ShouldCleanUpAsync(StoredChatSummary summary,
        Func<CancellationToken, Task<ChatDetails?>> loadChat, CancellationToken token)
    {
        if (summary.Title != Title) return false;
        return await loadChat(token) is { Messages.Count: <= InitialMessages } chat && chat.Kind == Kind.Value;
    }

    public Task OnHostStartedAsync(CancellationToken token) => Task.CompletedTask;
    public Task OnHostStoppingAsync(CancellationToken token) => Task.CompletedTask;
}
