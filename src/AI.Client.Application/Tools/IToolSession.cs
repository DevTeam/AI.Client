namespace AI.Client.Application.Tools;

using Contracts.Chat;

public sealed record AgentTool(ChatToolDefinition Definition, Guid ServerId, string OriginalName, string SchemaHash);

public interface IToolSession : IAsyncDisposable
{
    IReadOnlyList<AgentTool> Tools { get; }
    string ValidateArguments(AgentTool tool, string arguments);
    Task<string> CallAsync(AgentTool tool, string arguments, CancellationToken cancellationToken);
}

public interface IToolSessionFactory
{
    Task<IToolSession> OpenAsync(CancellationToken cancellationToken);
}
