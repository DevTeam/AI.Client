namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;
using Grants;

/// <summary>
/// Turns the session's directory grants into the <see cref="SandboxPolicy"/> that <c>process_run</c>
/// commands and <c>cs_run</c> scripts always run under. A grant that may write, edit or delete opens
/// its directory for writing, a read-only grant for reading. A non-recursive grant is left out: the OS
/// sandboxes open whole trees, and opening the tree would give more than the grant does. Nothing else
/// is opened on the program's behalf: a program that needs a directory outside the grants, a package
/// cache or an SDK in the user profile, gets it when the user grants it to the project.
/// </summary>
/// <remarks>
/// Commands keep the network: restoring packages and fetching sources are ordinary work for them.
/// Scripts run without it wherever the OS can close it. <c>process_run_unrestricted</c> is the way
/// past the sandbox, and it asks the user first.
/// </remarks>
public sealed class ToolSandbox : IToolSandbox
{
    private readonly IProcessLauncher _launcher;
    private readonly SandboxPolicy _commands;
    private readonly SandboxPolicy _scripts;

    public ToolSandbox(IPathGuard guard, IProcessLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _launcher = launcher;
        var paths = new List<SandboxPath>();
        foreach (var grant in guard.Grants.Where(grant => grant.Recursive))
        {
            var writes = grant.Capabilities.Overlaps([GrantCapability.Write, GrantCapability.Edit, GrantCapability.Delete]);
            paths.Add(new SandboxPath(grant.Root, writes ? SandboxAccess.ReadWrite : SandboxAccess.Read));
            if (grant.Purpose == "chatTemporary" && writes)
            {
                TemporaryDirectory ??= grant.Root;
            }
        }

        _commands = new SandboxPolicy(paths);
        _scripts = new SandboxPolicy(paths, AllowNetwork: !launcher.Support.ClosesNetwork);
    }

    public string? TemporaryDirectory { get; }

    public SandboxPolicy? Policy(SandboxedTool tool) => tool == SandboxedTool.Commands ? _commands : _scripts;

    public string Describe(SandboxedTool tool)
    {
        var support = _launcher.Support;
        if (!support.Available)
        {
            return $"The program must run in a sandbox, but {support.Mechanism} is unavailable on this machine ({support.Reason}), so every run fails"
                   + (tool == SandboxedTool.Commands ? "; process_run_unrestricted, which asks the user first, still works. " : ". ");
        }

        var network = Policy(tool)!.AllowNetwork
            ? tool == SandboxedTool.Scripts ? "The network is open: this machine cannot close it. " : "The network is open. "
            : "The network is closed: use fetch or process_run for anything online. ";
        return $"The program always runs in a {support.Mechanism} sandbox: it can read and write only the directories of the session's grants "
               + "(read-only grants only read; use list_allowed_directories) and the chat's temporary directory, write its own TEMP, "
               + "and read the OS and the .NET runtime. Anything else is denied, the user profile with its package caches and SDKs included, "
               + "and so is starting in a directory outside the grants. "
               + network
               + (support.OpensNullDevice ? "" : NullDeviceClosed(tool))
               + AccessDenied(tool);
    }

    /// <summary>What a run on a machine whose sandbox cannot open the null device should expect.</summary>
    public static string NullDeviceClosed(SandboxedTool tool) =>
        "On this machine Windows does not let sandboxed programs open the null device (NUL), and no directory grant changes "
        + "that: a program that opens it fails here, Git for Windows at every start (\"could not open '/dev/null'\"). "
        + (tool == SandboxedTool.Commands
            ? "Run such a program with process_run_unrestricted rather than asking for a grant. "
            : "Run such a program with process_run_unrestricted. ");

    /// <summary>What to do when the sandbox, rather than the program, is why a run failed.</summary>
    private static string AccessDenied(SandboxedTool tool) =>
        "When a run fails because a path outside the grants was denied (an access or permission error naming it, or a "
        + "missing SDK or package cache that does exist on the machine), that is the boundary, not a bug: ask the user "
        + "to grant that directory to the project, read-only unless it must be written, then retry; never work around it. "
        + (tool == SandboxedTool.Commands
            ? "When the user declines or the command must reach beyond any grant, process_run_unrestricted runs it without "
              + "the sandbox after the user approves the call. "
            : "");
}
