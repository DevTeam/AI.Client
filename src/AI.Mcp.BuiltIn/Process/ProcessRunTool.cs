using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI.Mcp.BuiltIn.Process;

/// <summary>
/// <c>process_run</c>: the default program runner, using the host user's permissions.
/// </summary>
[McpServerToolType]
public sealed class ProcessRunTool(IProcessSessions processes, IBuiltInToolReply reply)
    : ProcessRunToolBase(processes, reply)
{
    public const string ToolName = "process_run";

    protected override string Name => ToolName;

    protected override bool Sandboxed => false;

    protected override string Boundary =>
        "The default tool for commands, builds, tests and installed CLI tools. The program runs with the host user's permissions, "
        + "without a sandbox; workingDirectory and project directory grants do not confine it. "
        + "Calls follow the effective tool policy and saved command rules; subtasks cannot ask for approval. ";
}

/// <summary>
/// <c>process_run_sandboxed</c>: explicitly requested execution confined to the session's grants.
/// </summary>
[McpServerToolType]
public sealed class ProcessRunSandboxedTool(IProcessSessions processes, IBuiltInToolReply reply, IToolSandbox sandbox)
    : ProcessRunToolBase(processes, reply)
{
    public const string ToolName = "process_run_sandboxed";

    protected override string Name => ToolName;

    protected override bool Sandboxed => true;

    protected override string Boundary =>
        "For explicitly requested sandboxed execution. " + sandbox.Describe(SandboxedTool.Commands);
}

/// <summary>The program-running tool both variants share; only the sandbox and the wording differ.</summary>
public abstract class ProcessRunToolBase(IProcessSessions processes, IBuiltInToolReply reply) : IToolFactory
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
            Description = $"Run a program once, returning completion or a running processId after waitTimeoutMs. No implicit shell or interactive input. " +
                          "A running result is not an error: prefer trigger_wait type process_completed with processSessionId set to that ID and the returned stdoutOffset/stderrOffset for a longer completion wait; process_wait reads intermediate output. Do not restart or split a running test suite. " +
                          "Only a terminal result with exitCode establishes the outcome. process_stop stops this session's process tree. " +
                          "Full stdout/stderr byte logs are saved at stdoutPath/stderrPath in their original encoding, normally in the chat's temporary directory. " +
                          "Handles belong to this MCP session; closing it stops remaining processes. " +
                          $"Output is limited to {ProcessRunner.OutputLimit} characters per stream. " +
                          $"Host OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}). " +
                          $"Arguments are passed as a JSON array of strings; each element becomes one argv entry, with no shell parsing, " +
                          $"glob expansion, or environment-variable substitution. The process's standard input is closed immediately after launch. " +
                          $"timeoutMs is the hard process deadline, independent of waitTimeoutMs; reaching it kills the tree and returns timed_out. " +
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
        [Description("How long this call waits before returning status running and processId. The process continues under its original sandbox, limits and timeoutMs. 0 returns immediately; default 10000, maximum 60000.")] [Range(0, ProcessSessions.MaxWaitTimeoutMs)] int waitTimeoutMs = ProcessSessions.DefaultWaitTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            var limits = ChildProcessLimits.Create(memoryLimitMb, processLimit);
            result = await processes.RunAsync(
                new ProcessRequest(executable, arguments ?? [], workingDirectory ?? "", timeoutMs, limits, Sandboxed), waitTimeoutMs, cancellationToken);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            result = new ProcessResult(null, "", "", 0, false, false, error.Message, Status: "failed");
        }

        return reply.Reply(
            result,
            isError: result.Status != "running" && (result.Error is not null || result.TimedOut || result.ExitCode != 0));
    }
}
