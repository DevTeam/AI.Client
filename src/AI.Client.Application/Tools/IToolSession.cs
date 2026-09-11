namespace AI.Client.Application.Tools;

using Contracts.Chat;
using Contracts.Tools;

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

/// <summary>
/// One <c>notifications/progress</c> report from a server, as-is. A server may send none, so this
/// is an optional refinement of "still running" rather than something the UI can depend on.
/// </summary>
public sealed record ToolProgress(double Progress, double? Total, string? Message);

/// <summary>
/// What the Host is doing right now, reported as a call starts, whenever its progress changes, and
/// once more as null when it ends. Timestamping belongs to the caller, which owns the clock.
/// </summary>
public sealed record ToolActivity(
    string CallId,
    string Name,
    string Arguments,
    double? Progress = null,
    double? Total = null,
    string? Message = null);

/// <summary>A project directory grant handed to the tool server, which rejects file system paths outside of these roots.</summary>
public sealed record ToolDirectoryGrant(string Root, bool Recursive, IReadOnlyList<string> Capabilities);

public interface IToolSession : IAsyncDisposable
{
    IReadOnlyList<AgentTool> Tools { get; }
    string ValidateArguments(AgentTool tool, string arguments);
    Task<ToolCallResult> CallAsync(
        AgentTool tool,
        string arguments,
        IProgress<ToolProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IToolSessionFactory
{
    Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, CancellationToken cancellationToken);
}
