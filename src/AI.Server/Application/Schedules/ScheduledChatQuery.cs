namespace AI.Application.Schedules;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;

/// <summary>
/// The scheduled chats that come due soon, across every project, soonest first: what the sidebar
/// lists so an arriving run is visible before it starts. When a chat is due is the shared rule
/// <see cref="ScheduledChats"/>, so the sidebar and the dispatcher agree.
/// </summary>
public interface IScheduledChatQuery
{
    Task<IReadOnlyList<ScheduledChatSummary>> SoonAsync(TimeSpan horizon, int limit, CancellationToken cancellationToken);
}

public sealed class ScheduledChatQuery(
    IProjectService projects,
    IChatService chats,
    IChatScheduleStore store,
    IClock clock) : IScheduledChatQuery
{
    public async Task<IReadOnlyList<ScheduledChatSummary>> SoonAsync(TimeSpan horizon, int limit,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = new List<ScheduledChatSummary>();
        // Every project is walked, as the dispatcher does: a schedule of a project nobody has open
        // still runs, and so still belongs on the list.
        foreach (var project in await projects.ListAsync(cancellationToken))
        foreach (var summary in await chats.ListAsync(project.Id, cancellationToken))
        {
            if (summary.Kind != ChatSchedule.Kind || summary.ArchivedAt is not null) continue;
            if (await store.ReadAsync(project.Id, summary.Id, cancellationToken) is not { Schedule: { } schedule }) continue;
            if (ScheduledChats.DueAt(schedule) is { } moment) due.Add(new ScheduledChatSummary(summary, moment));
        }
        return ScheduledChats.Soon(due, now, horizon, limit);
    }
}
