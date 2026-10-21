namespace AI.Mcp.BuiltIn.Scripts;

using System.ComponentModel;
using System.Diagnostics;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;

/// <summary>
/// Keeps one worker started ahead of the next script. Starting .NET, loading Roslyn and compiling
/// the first script cost more than a second; a worker started after the previous script has done
/// all of that by the time the model sends the next one, and only compiles the script itself. A
/// worker still runs exactly one script, so scripts share nothing.
/// </summary>
/// <remarks>
/// The spare is started with the default limits; a call that asks for other limits gets a worker of
/// its own. It is in the same job and sandbox as any worker, and it ends with the server: on Windows
/// with its job, elsewhere when its standard input closes.
/// </remarks>
public sealed class ScriptWorkers(IProcessLauncher launcher, IToolSandbox sandbox) : IScriptWorkers, IDisposable
{
    /// <summary>The argument that makes the server executable run one script instead of serving MCP.</summary>
    public const string WorkerArgument = "--script-worker";

    private readonly Lock _gate = new();
    private ILaunchedProcess? _spare;

    public ILaunchedProcess Take(ChildProcessLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ILaunchedProcess? spare;
        lock (_gate)
        {
            spare = _spare;
            _spare = null;
        }

        if (spare is not null)
        {
            if (limits == ChildProcessLimits.Default && !spare.Process.HasExited)
            {
                return spare;
            }

            spare.Dispose();
        }

        return Start(limits);
    }

    public void Replenish()
    {
        lock (_gate)
        {
            if (_spare is not null)
            {
                return;
            }
        }

        ILaunchedProcess worker;
        try
        {
            worker = Start(ChildProcessLimits.Default);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            // The next call starts its own worker and reports the failure itself.
            return;
        }

        lock (_gate)
        {
            if (_spare is null)
            {
                _spare = worker;
                return;
            }
        }

        worker.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _spare?.Dispose();
            _spare = null;
        }
    }

    private ILaunchedProcess Start(ChildProcessLimits limits)
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Cannot locate the script worker executable.");
        var assembly = typeof(ScriptRunner).Assembly.Location;
        var directory = Path.GetDirectoryName(assembly) ?? AppContext.BaseDirectory;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // The script's own directory is set by the worker; the worker itself starts where it can read.
            WorkingDirectory = directory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(assembly);
        }

        start.ArgumentList.Add(WorkerArgument);
        var policy = sandbox.Policy;
        if (policy is not null)
        {
            // The worker loads the compiler and its own assemblies from the server's directory.
            policy = policy with { Paths = [.. policy.Paths, new SandboxPath(directory, SandboxAccess.Read)] };
            // The runtime's diagnostic channel lives in the user's temporary directory, which is closed.
            start.Environment["DOTNET_EnableDiagnostics"] = "0";
            if (sandbox.TemporaryDirectory is { } temporary)
            {
                foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
                {
                    start.Environment[name] = temporary;
                }
            }
        }

        return launcher.Start(new ProcessLaunch(start, policy, limits));
    }
}
