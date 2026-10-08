namespace AI.Application.Schedules;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;

/// <summary>
/// The scheduled chats the sidebar lists, across every project: the ones that come due soon, soonest
/// first, and under them the ones whose schedule has nothing left to do but has run, the run that
/// finished last first. What the sidebar shows so an arriving run is visible before it starts and
/// the run that just ended does not vanish the instant it does. When a chat is due is the shared
/// rule <see cref="ScheduledChats"/>, so the sidebar and the dispatcher agree.
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
        var listed = new List<ScheduledChatSummary>();
        // Every project is walked, as the dispatcher does: a schedule of a project nobody has open
        // still runs, and so still belongs on the list.
        foreach (var project in await projects.ListAsync(cancellationToken))
        foreach (var summary in await chats.ListAsync(project.Id, cancellationToken))
        {
            if (summary.Kind != ChatSchedule.Kind || summary.ArchivedAt is not null) continue;
            if (await store.ReadAsync(project.Id, summary.Id, cancellationToken) is not { Schedule: { } schedule }) continue;
            var due = ScheduledChats.DueAt(schedule);
            var finished = ScheduledChats.FinishedAt(schedule);
            // Nothing pending and nothing ever run: the guide's demo, or a schedule not started yet.
            if (due is null && finished is null) continue;
            listed.Add(new ScheduledChatSummary(summary, due, finished));
        }
        return ScheduledChats.Soon(listed, now, horizon, limit);
    }
}
