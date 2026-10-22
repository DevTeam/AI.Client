namespace AI.Infrastructure.Tools;

using ModelContextProtocol.Client;

/// <summary>A tool server process to start and talk to over its standard streams.</summary>
/// <param name="InheritEnvironment">
/// Whether the process inherits the Host's environment besides <paramref name="Environment"/>; null leaves
/// the transport's default.
/// </param>
/// <param name="ShutdownTimeout">
/// How long closing the session waits for the process to exit; the run's answer is not saved until it
/// is closed.
/// </param>
public sealed record ToolProcessLaunch(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IDictionary<string, string?> Environment,
    bool? InheritEnvironment,
    TimeSpan ShutdownTimeout);

/// <summary>
/// Where tool server processes run (ADR-014). The built-in server and user-configured stdio servers are
/// started only through this, so a container or a remote machine can stand in for this computer by
/// replacing one implementation.
/// </summary>
public interface IToolEnvironment
{
    IClientTransport Launch(ToolProcessLaunch launch);
}
