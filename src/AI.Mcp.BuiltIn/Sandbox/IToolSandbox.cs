namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;

/// <summary>The programs of the built-in server that run confined to the session's grants.</summary>
public enum SandboxedTool
{
    /// <summary><c>process_run</c> commands.</summary>
    Commands,

    /// <summary><c>cs_run</c> scripts.</summary>
    Scripts
}

/// <summary>
/// The sandbox the programs of this session run in. It opens exactly the session's directory grants;
/// <c>process_run_unrestricted</c> is the only way past it.
/// </summary>
public interface IToolSandbox
{
    /// <summary>The policy to start <paramref name="tool"/>'s programs with; <c>null</c> only outside a session.</summary>
    SandboxPolicy? Policy(SandboxedTool tool);

    /// <summary>
    /// A directory the confined programs may write temporary files to, given to them as their
    /// temporary directory; <c>null</c> when the session has none.
    /// </summary>
    string? TemporaryDirectory { get; }

    /// <summary>One paragraph for a tool description: what <paramref name="tool"/>'s programs may reach and what to do past it.</summary>
    string Describe(SandboxedTool tool);
}
