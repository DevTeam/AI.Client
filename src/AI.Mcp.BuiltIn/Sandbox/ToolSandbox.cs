namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;
using Grants;

/// <summary>
/// Turns the session's directory grants into a <see cref="SandboxPolicy"/> for each program the
/// Host switched the sandbox on for: commands through <see cref="CommandsVariable"/>, scripts
/// through <see cref="ScriptsVariable"/>. A grant that may write, edit or delete opens its directory
/// for writing, a read-only grant for reading. A non-recursive grant is left out: the OS sandboxes
/// open whole trees, and opening the tree would give more than the grant does.
/// </summary>
/// <remarks>
/// Commands keep the network: restoring packages and fetching sources are ordinary work for them.
/// Scripts run without it wherever the OS can close it, because a sandboxed script runs without
/// asking by default, and a script that could read the grants and send them anywhere unasked would
/// be a way out of the very boundary the sandbox draws.
/// </remarks>
public sealed class ToolSandbox : IToolSandbox
{
    /// <summary>Set to <c>1</c> by the Host to run commands confined to the grants.</summary>
    public const string CommandsVariable = "AI_CLIENT_SANDBOX";

    /// <summary>Set to <c>1</c> by the Host to run scripts confined to the grants.</summary>
    public const string ScriptsVariable = "AI_CLIENT_SCRIPT_SANDBOX";

    private readonly IProcessLauncher _launcher;
    private readonly SandboxPolicy? _commands;
    private readonly SandboxPolicy? _scripts;

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

        if (IsOn(CommandsVariable))
        {
            _commands = new SandboxPolicy(paths);
        }

        if (IsOn(ScriptsVariable))
        {
            _scripts = new SandboxPolicy(paths, AllowNetwork: !launcher.Support.ClosesNetwork);
        }
    }

    public string? TemporaryDirectory { get; }

    public SandboxPolicy? Policy(SandboxedTool tool) => tool == SandboxedTool.Commands ? _commands : _scripts;

    public string Describe(SandboxedTool tool)
    {
        if (Policy(tool) is not { } policy)
        {
            return "The program is not sandboxed: it can read and write anything the host user can. ";
        }

        var support = _launcher.Support;
        if (!support.Available)
        {
            return $"The sandbox is switched on but {support.Mechanism} is unavailable on this machine ({support.Reason}), so every run fails. ";
        }

        var network = policy.AllowNetwork
            ? tool == SandboxedTool.Scripts ? "The network is open: this machine cannot close it. " : "The network is open. "
            : "The network is closed: use fetch or process_run for anything online. ";
        return $"The program runs in a {support.Mechanism} sandbox: it can read and write only the directories of the session's grants "
               + "(read-only grants only read; use list_allowed_directories) and the chat's temporary directory, write its own TEMP, "
               + "and read the OS and the .NET runtime. Anything else, the user profile with its package caches included, is denied, "
               + "and so is starting in a directory outside the grants. "
               + network;
    }

    private static bool IsOn(string variable) => Environment.GetEnvironmentVariable(variable) is "1" or "true";
}
