namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

public interface IResourceModelProjection
{
    string Project(string content, IReadOnlyList<ChatResourceRef>? references);
}
