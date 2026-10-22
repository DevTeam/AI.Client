namespace AI.Application.Runs;

using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Settings;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Domain.Projects;
using AI.Domain.Chats;

/// <summary>
/// The chats the application guide uses: the hidden service chats its tours run in, and the
/// visible demo chats it can set up when a tour needs a conversation to point at. Both are the
/// guide's to remove once the tour is over.
/// </summary>
public interface IGuideChats
{
    /// <summary>
    /// The first <em>enabled</em> connection among the visible chat's, the project's, the default
    /// and any other. A chat's own choice stops at a disabled connection on purpose, but a guide
    /// only borrows one: it should run on whatever model works. Null when none is enabled.
    /// </summary>
    Guid? PickConnection(GlobalSettings settings, Guid? visibleChatConnection, Guid? projectConnection);

    /// <summary>
    /// A visible chat holding a question and its answer, written without a model, so that forking,
    /// editing as a branch, branch switching and comments have something to show. It is removed
    /// with the tour unless the person has added to it.
    /// </summary>
    Task<ChatDetails> CreateDemoAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// A visible chat holding a small team at work — a lead, two teammates, their reports and an open
    /// question — written without a model, so team work has something to show. It is removed with
    /// the tour unless the person has added to it.
    /// </summary>
    Task<ChatDetails> CreateTeamDemoAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// A visible scheduled chat with a few finished runs, written without a model, whose schedule
    /// starts nothing, so the Schedule widget, the run branches and their outcomes have something to
    /// show. It is removed with the tour unless the person changes it or writes in it.
    /// </summary>
    Task<ChatDetails> CreateScheduleDemoAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the project's guide chats that have no run in progress, except <paramref name="keep"/>,
    /// and its demo chats nobody has written in. A tour still running, in this window or another,
    /// is left alone.
    /// </summary>
    Task<int> CleanUpAsync(Guid projectId, Guid? keep, CancellationToken cancellationToken);
}

public sealed class GuideChats(IChatRepository repository, IChatRunDispatcher runs, IChatService chats, IAppDataChangeSignal changes,
    IConnectionChoice connectionChoice, IChatKindPolicyRegistry kindPolicies, AI.Application.Schedules.IScheduleDemo scheduleDemo) : IGuideChats
{
    public const string DemoTitle = DemoChatKindPolicy.Title;

    public Guid? PickConnection(GlobalSettings settings, Guid? visibleChatConnection, Guid? projectConnection) =>
        connectionChoice.Choose(settings.Connections, visibleChatConnection, projectConnection)?.Id;

    public async Task<ChatDetails> CreateDemoAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var chat = await chats.CreateAsync(projectId, new CreateChatRequest(DemoTitle, Kind: ChatKind.Demo.Value), cancellationToken);
        // The window learns about chats a tool made from this signal, and lists the new one.
        changes.Notify();
        return chat;
    }

    public async Task<ChatDetails> CreateScheduleDemoAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var chat = await scheduleDemo.CreateAsync(projectId, cancellationToken);
        changes.Notify();
        return chat;
    }

    public async Task<ChatDetails> CreateTeamDemoAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var chat = await chats.CreateAsync(projectId,
            new CreateChatRequest(TeamDemoChatKindPolicy.Title, Kind: ChatKind.TeamDemo.Value), cancellationToken);
        changes.Notify();
        return chat;
    }

    public async Task<int> CleanUpAsync(Guid projectId, Guid? keep, CancellationToken cancellationToken)
    {
        var summaries = (await repository.ListSummariesAsync(new ProjectId(projectId), cancellationToken))
            .Where(item => item.Id.Value != keep).ToArray();
        var guides = new List<StoredChatSummary>();
        foreach (var candidate in summaries)
        {
            if (kindPolicies.TryResolve(candidate.Kind) is { } policy
                && await policy.ShouldCleanUpAsync(candidate,
                    ct => chats.GetAsync(projectId, candidate.Id.Value, ct), cancellationToken))
                guides.Add(candidate);
        }
        if (guides.Count == 0) return 0;
        var running = (await runs.GetSnapshotAsync(cancellationToken))
            .Where(run => run.ProjectId == projectId && run.Status == ChatRunStatus.Generating)
            .Select(run => run.ChatId).ToHashSet();
        var deleted = 0;
        foreach (var guide in guides.Where(item => !running.Contains(item.Id.Value)))
        {
            try
            {
                if ((await runs.DeleteChatAsync(projectId, guide.Id.Value, guide.Revision, cancellationToken)).IsDeleted) deleted++;
            }
            // Another change got there first; the next clean-up will find it again.
            catch (InvalidOperationException) { }
        }
        if (deleted > 0) changes.Notify();
        return deleted;
    }
}
