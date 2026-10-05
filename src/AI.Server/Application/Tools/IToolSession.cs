namespace AI.Application.Tools;

using Chat;
using Contracts.Tools;
using AI.Domain.Chats;
using System.Text.Json;

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

/// <summary>
/// Which run a tool session was opened for. A session is opened per run, so this is handed down at
/// connection time rather than asked for per call: a tool that acts on the conversation it was
/// called from should not have to be told which one that is by the model, which can be wrong about
/// it or can name somebody else's.
/// </summary>
/// <param name="Interactive">
/// Whether a person can be reached from this run. False for a run nobody is watching — a subtask
/// delegating work in the background — where anything that would stop to ask is answered "no" at
/// once instead of waiting out a timeout nobody will interrupt.
/// </param>
public sealed record ToolRunContext(Guid ProjectId, Guid ChatId, Guid BranchId, bool Interactive,
    ChatKind Kind = default, JsonElement? KindState = null, int KindStateVersion = 1,
    bool OverlayPromptsAllowed = true)
{
    /// <summary>
    /// No run at all: a session opened to inspect what the servers offer, never to call anything.
    /// Not interactive, because there is no conversation for an answer to return to.
    /// </summary>
    public static readonly ToolRunContext None = new(Guid.Empty, Guid.Empty, Guid.Empty, false);
}

/// <summary>
/// Where one call sits in the batch the model asked for, counting from one. Carried into approval
/// so a user deciding on the first of several is told there are more, rather than discovering it
/// one prompt at a time.
/// </summary>
public sealed record ToolCallPosition(int Index, int BatchSize);

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
    /// <summary>
    /// Connects to the MCP servers named in <paramref name="servers"/> and presents their tools as
    /// one set. Enablement is decided by the caller from settings, because a server that is off
    /// must not be started at all — not started and then filtered out.
    /// </summary>
    Task<IToolSession> OpenAsync(
        IReadOnlyList<ToolDirectoryGrant> directoryGrants,
        IReadOnlySet<Guid> servers,
        ToolRunContext run,
        CancellationToken cancellationToken);
}

/// <summary>
/// One MCP server the Host knows how to reach. Directory grants are passed to every connection;
/// a server that has no use for them ignores them.
/// </summary>
public interface IMcpServerConnection
{
    Guid ServerId { get; }

    Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, ToolRunContext run, CancellationToken cancellationToken);
}
