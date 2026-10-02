namespace AI.Application.Runs;

using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Settings;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Domain.Projects;

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
    /// Deletes the project's guide chats that have no run in progress, except <paramref name="keep"/>,
    /// and its demo chats nobody has written in. A tour still running, in this window or another,
    /// is left alone.
    /// </summary>
    Task<int> CleanUpAsync(Guid projectId, Guid? keep, CancellationToken cancellationToken);
}

public sealed class GuideChats(IChatRepository repository, IChatRunDispatcher runs, IChatService chats, IAppDataChangeSignal changes,
    IConnectionChoice connectionChoice) : IGuideChats
{
    /// <summary>Marks a demo chat; ordinary chats keep the default mode, so a chat a person named alike is never taken for one.</summary>
    public const string DemoMode = "demo";
    public const string DemoTitle = "Guide demo";
    private const string DemoQuestion = "Suggest a name for a small weather app.";
    private const string DemoAnswer = "Three ideas:\n\n1. **Skyline**: short and calm.\n2. **Drizzle**: light and playful.\n"
        + "3. **Forecastly**: says what it does.\n\nThis chat was set up by the application guide to show forking, branches and "
        + "comments. It is removed when the tour ends unless you write in it.";
    private const int DemoMessages = 2;

    public Guid? PickConnection(GlobalSettings settings, Guid? visibleChatConnection, Guid? projectConnection) =>
        connectionChoice.Choose(settings.Connections, visibleChatConnection, projectConnection)?.Id;

    public async Task<ChatDetails> CreateDemoAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var chat = await chats.CreateAsync(projectId, new CreateChatRequest(DemoTitle, GuideMode: DemoMode), cancellationToken);
        var questionId = Guid.CreateVersion7();
        chat = await chats.AppendMessageAsync(projectId, chat.Id,
                   new AppendChatMessageRequest(questionId, null, "User", DemoQuestion, chat.Revision), cancellationToken)
               ?? throw new InvalidOperationException("The demo chat could not be written.");
        chat = await chats.AppendMessageAsync(projectId, chat.Id,
                   new AppendChatMessageRequest(Guid.CreateVersion7(), questionId, "Assistant", DemoAnswer, chat.Revision), cancellationToken)
               ?? throw new InvalidOperationException("The demo chat could not be written.");
        // The window learns about chats a tool made from this signal, and lists the new one.
        changes.Notify();
        return chat;
    }

    public async Task<int> CleanUpAsync(Guid projectId, Guid? keep, CancellationToken cancellationToken)
    {
        var summaries = (await repository.ListSummariesAsync(new ProjectId(projectId), cancellationToken))
            .Where(item => item.Id.Value != keep).ToArray();
        var guides = summaries.Where(item => item.IsGuide).ToList();
        foreach (var candidate in summaries.Where(item => !item.IsGuide && item.Title == DemoTitle))
        {
            // Only a demo the person left as it was goes: one they wrote in is theirs now.
            if (await chats.GetAsync(projectId, candidate.Id.Value, cancellationToken) is { GuideMode: DemoMode } demo
                && demo.Messages.Count <= DemoMessages)
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
