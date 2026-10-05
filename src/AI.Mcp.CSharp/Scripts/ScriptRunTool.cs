namespace AI.Mcp.CSharp.Scripts;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ScriptRunTool(IScriptRunner runner, IToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        RunAsync,
        new McpServerToolCreateOptions
        {
            Description = "Compile and run a C# script in this server's own process with Microsoft.CodeAnalysis.Scripting (Roslyn). "
                          + "The code is the body of a script, so top-level statements, `using` directives, and the last expression as the result are allowed. "
                          + "Everything the script writes to the console comes back in `stdout`/`stderr`, and the values of its variables come back in `variables`. "
                          + "This is not a sandbox: `workingDirectory` and `environment` are set for the duration of the run and restored afterwards, but the script can "
                          + $"otherwise reach whatever this process can, exactly like process_run. Output is limited to {ScriptLimits.OutputCharacters} characters per stream "
                          + $"and the run to {ScriptLimits.MaxTimeoutMs} ms, after which the result is reported as timed out. "
                          + $"Host OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})."
        });

    [McpServerTool(Name = "cs_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(ScriptResult))]
    private async Task<CallToolResult> RunAsync(
        [Description("C# script code: top-level statements, optional `using` directives, and an optional trailing expression whose value is returned as `returnValue`. Example: `var total = Args.Length; Console.WriteLine(total); total * 2`")]
        [MaxLength(262144)] string code,
        [Description("Values exposed to the script as the string array `Args`, for example command-line-like inputs. Empty array or null means no arguments.")]
        [MaxLength(256)] string[]? arguments = null,
        [Description("Additional namespaces to import on top of the defaults (System, System.IO, System.Linq, System.Text, System.Text.Json, System.Text.RegularExpressions, System.Threading, System.Threading.Tasks and others).")]
        [MaxLength(64)] string[]? imports = null,
        [Description("Assembly references the script needs: an absolute path to an assembly file, or an assembly name (simple or full) that is already loaded in this process, for example `System.Xml.Linq`. A reference that cannot be resolved is skipped with a warning in `diagnostics`.")]
        [MaxLength(64)] string[]? references = null,
        [Description("Working directory for the script, applied as the process current directory for the duration of the run and restored afterwards. Not a sandbox. Absolute path; empty means leave the current directory unchanged.")]
        [MaxLength(4096)] string? workingDirectory = null,
        [Description("Environment variables to set for the duration of the run and restore afterwards. Empty or null means the environment is left unchanged.")]
        [MaxLength(64)] Dictionary<string, string>? environment = null,
        [Description("Extra values exposed to the script as `Globals`, a read-only dictionary of raw JSON elements the script reads with JsonElement. Empty or null means an empty dictionary.")]
        [MaxLength(64)] Dictionary<string, JsonElement>? globals = null,
        [Description("Timeout in milliseconds before the run is reported as timed out. Must be in [1, 600000]; defaults to 600000. Omit it to use the configured tool timeout; set a smaller value only when the script needs a shorter deadline.")]
        [Range(1, ScriptLimits.MaxTimeoutMs)] int timeoutMs = ScriptLimits.DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return reply.Reply(new ScriptResult(false, null, null, [], "", "", [], 0, false, false, "Script code is empty."), true);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && !Path.IsPathFullyQualified(workingDirectory))
        {
            return reply.Reply(new ScriptResult(false, null, null, [], "", "", [], 0, false, false,
                $"Working directory must be absolute: {workingDirectory}"), true);
        }

        var request = new ScriptRequest(
            code,
            arguments ?? [],
            imports ?? [],
            references ?? [],
            workingDirectory ?? "",
            environment ?? new Dictionary<string, string>(StringComparer.Ordinal),
            globals ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            timeoutMs);

        ScriptResult result;
        try
        {
            result = await runner.RunAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            result = new ScriptResult(false, null, null, [], "", "", [], 0, false, false, error.Message);
        }

        return reply.Reply(result, isError: !result.Success);
    }
}
