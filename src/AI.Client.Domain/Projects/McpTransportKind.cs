// ReSharper disable UnusedMember.Global

namespace AI.Client.Domain.Projects;

public enum McpTransportKind
{
    Stdio,
    StreamableHttp,

    /// <summary>
    /// A server the Host runs inside its own process, reached over in-memory pipes rather than a
    /// child process's streams. Appended last on purpose: stored documents hold these by number.
    /// </summary>
    InProcess
}
