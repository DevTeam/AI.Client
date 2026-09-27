using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI.Mcp.BuiltIn.Process;

[McpServerToolType]
public sealed class ProcessRunTool(IProcessRunner processRunner, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        RunAsync,
        new McpServerToolCreateOptions
        {
            Description = $"Run a program and wait for completion. No implicit shell or interactive input. " +
                          $"The working directory is not a sandbox. Output is limited to {ProcessRunner.OutputLimit} characters per stream. " +
                          $"Host OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}). " +
                          $"Arguments are passed as a JSON array of strings; each element becomes one argv entry, with no shell parsing, " +
                          $"glob expansion, or environment-variable substitution. The process's standard input is closed immediately after launch. " +
                          $"On timeout, cancellation, or hard error the process tree is killed."
        });

    [McpServerTool(Name = "process_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(ProcessResult))]
    private async Task<CallToolResult> RunAsync(
        [Description("Path to the executable to run. A name with no directory separator is always resolved through PATH, even when `workingDirectory` is given — pass `./name` or an absolute path to run a file inside `workingDirectory`. A relative path containing a separator is resolved against `workingDirectory`; an absolute path is used as is.")] [MaxLength(4096)] string executable,
        [Description("Command-line arguments passed to the executable. Each element becomes one argv entry, with no shell parsing, glob expansion, or environment-variable substitution. Empty array or null means no extra arguments.")] [MaxLength(256)] string[]? arguments = null,
        [Description("Working directory for the process. Not a sandbox — the process can read and write anything the host user can. Empty means inherit the server's working directory.")] [MaxLength(4096)] string? workingDirectory = null,
        [Description("Timeout in milliseconds before the process tree is killed. Must be in [1, 120000].")] [Range(1, 120000)] int timeoutMs = 120000,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(new ProcessRequest(executable, arguments ?? [], workingDirectory ?? "", timeoutMs), cancellationToken);
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
