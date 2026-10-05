namespace AI.Application.Resources;

using AI.Contracts.Resources;

public interface IResourceModelProjection
{
    string Project(string content, IReadOnlyList<ChatResource>? references);
    Task<string> ProjectAsync(Guid projectId, Guid chatId, string content,
        IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken);
}
