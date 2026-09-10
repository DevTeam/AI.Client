namespace AI.Client.Application.Tools;

using Contracts.Chat;

public sealed record AgentTool(ChatToolDefinition Definition, Guid ServerId, string OriginalName, string SchemaHash);

/// <summary>A project directory grant handed to the tool server, which rejects file system paths outside of these roots.</summary>
public sealed record ToolDirectoryGrant(string Root, bool Recursive, IReadOnlyList<string> Capabilities);

public interface IToolSession : IAsyncDisposable
{
    IReadOnlyList<AgentTool> Tools { get; }
    string ValidateArguments(AgentTool tool, string arguments);
    Task<string> CallAsync(AgentTool tool, string arguments, CancellationToken cancellationToken);
}

public interface IToolSessionFactory
{
    Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, CancellationToken cancellationToken);
}
