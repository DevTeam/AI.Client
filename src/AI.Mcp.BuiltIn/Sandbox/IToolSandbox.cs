namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;

/// <summary>
/// The sandbox the programs of this session run in: <c>process_run</c> commands and <c>cs_run</c>
/// scripts. It is switched on by the Host and opens exactly the session's directory grants.
/// </summary>
public interface IToolSandbox
{
    /// <summary>The policy to start programs with, or <c>null</c> when the sandbox is off.</summary>
    SandboxPolicy? Policy { get; }

    /// <summary>
    /// A directory the confined programs may write temporary files to, given to them as their
    /// temporary directory; <c>null</c> when there is none or the sandbox is off.
    /// </summary>
    string? TemporaryDirectory { get; }

    /// <summary>One sentence for a tool description that says what the programs may reach.</summary>
    string Describe();
}
