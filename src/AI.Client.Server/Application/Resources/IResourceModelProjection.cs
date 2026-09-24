namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

public interface IResourceModelProjection
{
    string Project(string content, IReadOnlyList<ChatResourceRef>? references);
    Task<string> ProjectAsync(Guid projectId, Guid chatId, string content,
        IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken);
}
