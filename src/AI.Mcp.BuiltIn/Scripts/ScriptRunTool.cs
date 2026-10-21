namespace AI.Mcp.BuiltIn.Scripts;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json;
using AI.Contracts.FileSystem;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ScriptRunTool(IScriptRunner runner, IBuiltInToolReply reply, IPath paths, IToolSandbox sandbox) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        RunAsync,
        new McpServerToolCreateOptions
        {
            Description = "Compile and run a C# script with Roslyn scripting on .NET, in a fresh child process per call. "
                          + "Use it for calculations, data transformations, parsing and checks that a few lines of C# do more reliably than reasoning or a shell pipeline; "
                          + "prefer the file tools for plain reads and edits and process_run for existing programs. "
                          + "The code is the body of a script: top-level statements, `using` directives, and a last expression without a semicolon, whose value is `returnValue`. "
                          + "Pass data through `arguments` (the string array `Args`) or `globals` (`Globals`, a dictionary of JsonElement) rather than pasting it into the code. "
                          + "Calls share no state. Everything the script writes to the console comes back in `stdout`/`stderr`, and the values of its variables in `variables`. "
                          + $"`workingDirectory` and `environment` apply to the run. Output is limited to {ScriptLimits.OutputCharacters} characters per stream "
                          + $"and the run to {ScriptLimits.MaxTimeoutMs} ms, after which the result is reported as timed out. "
                          + $"Host OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}. "
                          + sandbox.Describe()
                          + ChildProcessLimits.Describe()
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
        [Description("Working directory for the script, applied as the current directory of the run. It is not a boundary by itself; the tool description says what the script may reach. Absolute path; empty means the server's directory.")]
        [MaxLength(4096)] string? workingDirectory = null,
        [Description("Environment variables to set for the run. Empty or null means no additional variables.")]
        [MaxLength(64)] Dictionary<string, string>? environment = null,
        [Description("Extra values exposed to the script as `Globals`, a read-only dictionary of raw JSON elements the script reads with JsonElement. Empty or null means an empty dictionary.")]
        [MaxLength(64)] Dictionary<string, JsonElement>? globals = null,
        [Description("Timeout in milliseconds before the run is reported as timed out. Must be in [1, 86400000]; defaults to 600000. Omit it to use the configured tool timeout; set a smaller value only when the script needs a shorter deadline.")]
        [Range(1, ScriptLimits.MaxTimeoutMs)] int timeoutMs = ScriptLimits.DefaultTimeoutMs,
        [Description("Committed memory, in megabytes, the child process and everything it starts may use (Windows only); the compiler itself needs a few hundred. Omit it for the machine's default, stated in the tool description; raise it only when a previous run reported `limitExceeded: \"memory\"` or the script is known to need more.")]
        [Range(ChildProcessLimits.MinimumMemoryMb, ChildProcessLimits.SchemaMaximumMemoryMb)] int? memoryLimitMb = null,
        [Description("Processes that may be alive at once in the tree, the child process itself included (Windows only). Omit it for the default; raise it only when a previous run reported `limitExceeded: \"processes\"`.")]
        [Range(1, ChildProcessLimits.MaximumProcesses)] int? processLimit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return reply.Reply(new ScriptResult(false, null, null, [], "", "", [], 0, false, false, "Script code is empty."), true);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && !paths.IsFullyQualified(workingDirectory))
        {
            return reply.Reply(new ScriptResult(false, null, null, [], "", "", [], 0, false, false,
                $"Working directory must be absolute: {workingDirectory}"), true);
        }

        ChildProcessLimits limits;
        try
        {
            limits = ChildProcessLimits.Create(memoryLimitMb, processLimit);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return reply.Reply(new ScriptResult(false, null, null, [], "", "", [], 0, false, false, error.Message), true);
        }

        var request = new ScriptRequest(
            code,
            arguments ?? [],
            imports ?? [],
            references ?? [],
            workingDirectory ?? "",
            environment ?? new Dictionary<string, string>(StringComparer.Ordinal),
            globals ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            timeoutMs,
            limits);

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
