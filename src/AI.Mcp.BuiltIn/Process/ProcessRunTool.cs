using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI.Mcp.BuiltIn.Process;

/// <summary>
/// <c>process_run</c>: a program confined to the session's grants. It is safe to run without asking,
/// so it is allowed by default and works in subtasks, which cannot ask.
/// </summary>
[McpServerToolType]
public sealed class ProcessRunTool(IProcessRunner processRunner, IBuiltInToolReply reply, IToolSandbox sandbox)
    : ProcessRunToolBase(processRunner, reply)
{
    public const string ToolName = "process_run";

    protected override string Name => ToolName;

    protected override bool Sandboxed => true;

    protected override string Boundary => sandbox.Describe(SandboxedTool.Commands);
}

/// <summary>
/// <c>process_run_unrestricted</c>: the same program without the sandbox, for what the grants cannot
/// cover. It reaches everything the user's account can, so every call is asked about by default.
/// </summary>
[McpServerToolType]
public sealed class ProcessRunUnrestrictedTool(IProcessRunner processRunner, IBuiltInToolReply reply)
    : ProcessRunToolBase(processRunner, reply)
{
    public const string ToolName = "process_run_unrestricted";

    protected override string Name => ToolName;

    protected override bool Sandboxed => false;

    protected override string Boundary =>
        "The program runs without any sandbox and can read and write anything the host user can, the whole user profile included. "
        + "Use it only when process_run cannot do the job because the program needs a path outside the grants that the user did not grant; "
        + "first prefer asking the user to grant the directory to the project and using process_run. Every call asks the user to approve it "
        + "unless a saved rule allows it, and subtasks cannot ask. ";
}

/// <summary>The program-running tool both variants share; only the sandbox and the wording differ.</summary>
public abstract class ProcessRunToolBase(IProcessRunner processRunner, IBuiltInToolReply reply) : IToolFactory
{
    protected abstract string Name { get; }

    protected abstract bool Sandboxed { get; }

    /// <summary>What the program may reach, for the tool description.</summary>
    protected abstract string Boundary { get; }

    public McpServerTool Create() => McpServerTool.Create(
        RunAsync,
        new McpServerToolCreateOptions
        {
            Name = Name,
            Description = $"Run a program and wait for completion. No implicit shell or interactive input. " +
                          $"Output is limited to {ProcessRunner.OutputLimit} characters per stream. " +
                          $"Host OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}). " +
                          $"Arguments are passed as a JSON array of strings; each element becomes one argv entry, with no shell parsing, " +
                          $"glob expansion, or environment-variable substitution. The process's standard input is closed immediately after launch. " +
                          $"On timeout, cancellation, or hard error the process tree is killed. " +
                          Boundary +
                          ChildProcessLimits.Describe()
        });

    [McpServerTool(Name = ProcessRunTool.ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(ProcessResult))]
    private async Task<CallToolResult> RunAsync(
        [Description("Path to the executable to run. A name with no directory separator is always resolved through PATH, even when `workingDirectory` is given — pass `./name` or an absolute path to run a file inside `workingDirectory`. A relative path containing a separator is resolved against `workingDirectory`; an absolute path is used as is.")] [MaxLength(4096)] string executable,
        [Description("Command-line arguments passed to the executable. Each element becomes one argv entry, with no shell parsing, glob expansion, or environment-variable substitution. Empty array or null means no extra arguments.")] [MaxLength(256)] string[]? arguments = null,
        [Description("Working directory for the process. It is not a boundary by itself; the tool description says what the process may reach. Empty means inherit the server's working directory.")] [MaxLength(4096)] string? workingDirectory = null,
        [Description("Timeout in milliseconds before the process tree is killed. Must be in [1, 86400000]; defaults to 600000. Omit it to use the configured tool timeout; set a smaller value only when the command needs a shorter deadline.")] [Range(1, ProcessRunner.MaxTimeoutMs)] int timeoutMs = ProcessRunner.DefaultTimeoutMs,
        [Description("Committed memory, in megabytes, the whole process tree may use (Windows only). Omit it for the machine's default, stated in the tool description; raise it only when a previous run reported `limitExceeded: \"memory\"` or the program is known to need more.")] [Range(ChildProcessLimits.MinimumMemoryMb, ChildProcessLimits.SchemaMaximumMemoryMb)] int? memoryLimitMb = null,
        [Description("Processes that may be alive at once in the tree, the program itself included (Windows only). Omit it for the default; raise it only when a previous run reported `limitExceeded: \"processes\"`.")] [Range(1, ChildProcessLimits.MaximumProcesses)] int? processLimit = null,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            var limits = ChildProcessLimits.Create(memoryLimitMb, processLimit);
            result = await processRunner.RunAsync(
                new ProcessRequest(executable, arguments ?? [], workingDirectory ?? "", timeoutMs, limits, Sandboxed), cancellationToken);
        }
        catch (ArgumentException error)
        {
            result = new ProcessResult(null, "", "", 0, false, false, error.Message);
        }

        return reply.Reply(
            result,
            isError: result.Error is not null || result.TimedOut || result.ExitCode != 0);
    }
}
