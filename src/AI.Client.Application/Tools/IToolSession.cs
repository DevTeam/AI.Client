namespace AI.Client.Application.Tools;

using Contracts.Chat;

/// <summary>
/// A tool as the Host sees it: the function definition the provider is allowed to see, and the
/// full descriptor everything else works from. Keeping the two apart is what stops presentation
/// metadata from leaking into the model's tool list, and descriptor loss from reaching the UI.
/// </summary>
public sealed record AgentTool(
    ChatToolDefinition ModelDefinition,
    ToolDescriptor Descriptor,
    Guid ServerId,
    string OriginalName,
    string SchemaHash);

/// <summary>A project directory grant handed to the tool server, which rejects file system paths outside of these roots.</summary>
public sealed record ToolDirectoryGrant(string Root, bool Recursive, IReadOnlyList<string> Capabilities);

public interface IToolSession : IAsyncDisposable
{
    IReadOnlyList<AgentTool> Tools { get; }
    string ValidateArguments(AgentTool tool, string arguments);
    Task<AgentToolResult> CallAsync(AgentTool tool, string arguments, CancellationToken cancellationToken);
}

public interface IToolSessionFactory
{
    Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, CancellationToken cancellationToken);
}
