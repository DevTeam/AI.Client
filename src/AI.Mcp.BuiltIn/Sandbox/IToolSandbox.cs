namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;

/// <summary>The programs of the built-in server, each with a sandbox switch of its own.</summary>
public enum SandboxedTool
{
    /// <summary><c>process_run</c> commands.</summary>
    Commands,

    /// <summary><c>cs_run</c> scripts.</summary>
    Scripts
}

/// <summary>
/// The sandbox the programs of this session run in. The Host switches it on separately for
/// commands and for scripts, and it opens exactly the session's directory grants.
/// </summary>
public interface IToolSandbox
{
    /// <summary>The policy to start <paramref name="tool"/>'s programs with, or <c>null</c> when its sandbox is off.</summary>
    SandboxPolicy? Policy(SandboxedTool tool);

    /// <summary>
    /// A directory the confined programs may write temporary files to, given to them as their
    /// temporary directory; <c>null</c> when the session has none.
    /// </summary>
    string? TemporaryDirectory { get; }

    /// <summary>One sentence for a tool description that says what <paramref name="tool"/>'s programs may reach.</summary>
    string Describe(SandboxedTool tool);
}
