namespace AI.Web.Chats;

using AI.Contracts.Chats;
using Projects;

public sealed class SenderLocations(IProjectApi projects, IChatHistoryApi chats) : ISenderLocations
{
    // Long enough for one transcript render to share a read, short enough for a rename to show up.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly Dictionary<(Guid ProjectId, Guid ChatId), (DateTimeOffset ReadAt, Task<(string? Project, ChatDetails? Chat)> Read)> _reads = [];

    public async Task<string?> GetNameAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var key = (projectId, chatId);
        var now = DateTimeOffset.UtcNow;
        if (!_reads.TryGetValue(key, out var entry) || now - entry.ReadAt > Lifetime || entry.Read.IsFaulted)
            _reads[key] = entry = (now, ReadAsync(projectId, chatId));
        try
        {
            var (project, chat) = await entry.Read.WaitAsync(cancellationToken);
            return chat is null ? null : $"{project ?? "Project"} / {chat.Title} / {chat.SenderName(new MessageSender(chatId, branchId))}";
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public string GetLink(Guid projectId, Guid chatId, Guid branchId) =>
        $"aiclient://navigate/branch?projectId={projectId}&chatId={chatId}&branchId={branchId}";

    private async Task<(string? Project, ChatDetails? Chat)> ReadAsync(Guid projectId, Guid chatId)
    {
        // Shared by every caller, so no single caller's cancellation ends it.
        var project = projects.GetAsync(projectId, CancellationToken.None);
        var chat = chats.GetAsync(projectId, chatId, CancellationToken.None);
        return ((await project)?.Name, await chat);
    }
}
