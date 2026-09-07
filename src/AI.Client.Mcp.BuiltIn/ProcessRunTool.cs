using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI.Client.Mcp.BuiltIn;

[McpServerToolType]
public static class ProcessRunTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "process_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(ProcessResult))]
    [Description("Run a program and wait for completion. No implicit shell or interactive input. The working directory is not a sandbox. Output is limited to 32768 characters per stream.")]
    public static async Task<CallToolResult> RunAsync(
        [Description("Path to the executable to run.")] [MaxLength(4096)] string executable,
        [Description("Command-line arguments passed to the executable.")] [MaxLength(256)] string[]? arguments = null,
        [Description("Working directory for the process. Not a sandbox.")] [MaxLength(4096)] string? workingDirectory = null,
        [Description("Timeout in milliseconds before the process is killed.")] [Range(1, 120000)] int timeoutMs = 120000,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(new ProcessRequest(executable, arguments ?? [], workingDirectory ?? "", timeoutMs), cancellationToken);
        }
        catch (ArgumentException error)
        {
            result = new ProcessResult(null, "", "", 0, false, false, error.Message);
        }
        var structured = JsonSerializer.SerializeToElement(result, Json);
        return new CallToolResult
        {
            StructuredContent = structured,
            Content = [new TextContentBlock { Text = structured.GetRawText() }],
            IsError = result.Error is not null || result.TimedOut || result.ExitCode != 0
        };
    }
}
