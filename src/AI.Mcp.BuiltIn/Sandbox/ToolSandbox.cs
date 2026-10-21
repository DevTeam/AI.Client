namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;
using Grants;

/// <summary>
/// Turns the session's directory grants into a <see cref="SandboxPolicy"/> when the Host switched
/// the sandbox on through <see cref="Variable"/>. A grant that may write, edit or delete opens its
/// directory for writing, a read-only grant for reading. A non-recursive grant is left out: the OS
/// sandboxes open whole trees, and opening the tree would give more than the grant does.
/// </summary>
public sealed class ToolSandbox : IToolSandbox
{
    /// <summary>Set to <c>1</c> by the Host to run commands and scripts confined to the grants.</summary>
    public const string Variable = "AI_CLIENT_SANDBOX";

    private readonly IProcessLauncher _launcher;

    public ToolSandbox(IPathGuard guard, IProcessLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _launcher = launcher;
        if (Environment.GetEnvironmentVariable(Variable) is not ("1" or "true"))
        {
            return;
        }

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

        Policy = new SandboxPolicy(paths);
    }

    public SandboxPolicy? Policy { get; }

    public string? TemporaryDirectory { get; }

    public string Describe()
    {
        if (Policy is null)
        {
            return "The program is not sandboxed: it can read and write anything the host user can. ";
        }

        var support = _launcher.Support;
        return support.Available
            ? $"The program runs in a {support.Mechanism} sandbox: it can read and write only the directories of the session's grants "
              + "(read-only grants only read; use list_allowed_directories) and the chat's temporary directory, write its own TEMP, "
              + "and read the OS and the .NET runtime. Anything else, the user profile with its package caches included, is denied, "
              + "and so is starting in a directory outside the grants. The network is open. "
            : $"The sandbox is switched on but {support.Mechanism} is unavailable on this machine ({support.Reason}), so every run fails. ";
    }
}
