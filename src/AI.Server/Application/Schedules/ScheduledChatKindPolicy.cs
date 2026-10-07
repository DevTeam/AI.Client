namespace AI.Application.Schedules;

using System.Text.Json;
using AI.Application.Chats;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;
using AI.Domain.Chats;

/// <summary>
/// A chat that runs on a schedule. It behaves as an ordinary conversation in every respect; what
/// it adds is its state — the schedule — and the dispatcher, which this policy starts and stops
/// with the Host.
/// </summary>
/// <remarks>
/// The scheduler arrives as a factory: it depends on the run dispatcher, which depends on the
/// registry this policy belongs to.
/// </remarks>
public sealed class ScheduledChatKindPolicy(IChatScheduleStore store, IScheduleCalendar calendar, Func<IChatScheduler> scheduler,
    Func<IScheduleDemo> demo)
    : IChatKindPolicy
{
    public ChatKind Kind => ChatKind.Scheduled;
    public ChatKindBehavior Behavior { get; } = new();

    public void ValidateState(JsonElement? state, int version)
    {
        if (version != ChatSchedule.StateVersion) throw new ArgumentException("Unsupported schedule state version.");
        var schedule = store.Parse(state) ?? throw new ArgumentException("A scheduled chat needs a schedule.");
        if (calendar.Validate(schedule.Settings) is { } error) throw new ArgumentException(error);
    }

    public Task<ChatDetails> InitializeAsync(ChatDetails chat, IChatService chats, CancellationToken token) => Task.FromResult(chat);
    public string? NavigationMode(JsonElement? state) => null;
    public bool AllowsServer(Guid serverId) => true;
    public bool AllowsTool(Guid serverId, string toolName) => true;

    /// <summary>Only the guide's demo is cleaned up, and only while nobody has changed or written in it.</summary>
    public async Task<bool> ShouldCleanUpAsync(StoredChatSummary summary,
        Func<CancellationToken, Task<ChatDetails?>> loadChat, CancellationToken token) =>
        summary.Title == ScheduleDemo.Title && await loadChat(token) is { } chat && demo().IsUntouched(chat);

    public Task OnHostStartedAsync(CancellationToken token)
    {
        scheduler().Start();
        return Task.CompletedTask;
    }

    public Task OnHostStoppingAsync(CancellationToken token) => scheduler().StopAsync(token);
}
