namespace AI.Mcp.CSharp.Scripts;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

/// <summary>
/// Runs one C# script in this process with Roslyn scripting. Everything the run needs — code,
/// imports, assembly references, arguments, globals, working directory, environment and a timeout —
/// arrives in a single <see cref="ScriptRequest"/>, and everything it produced comes back in a
/// single <see cref="ScriptResult"/>.
///
/// This is not a sandbox. Like <c>process_run</c>, a script can reach whatever the server process
/// can: the file system, the network, the environment. The working directory and the environment
/// handed to the script are a convenience for the code being written, not a boundary.
/// </summary>
public sealed class ScriptRunner : IScriptRunner
{
    /// <summary>
    /// Every assembly of the runtime the server itself runs on, resolved once. Roslyn's
    /// <see cref="ScriptOptions.Default"/> references almost nothing — a script written with the
    /// default imports (<c>System.Linq</c>, <c>System.Text.Json</c>, …) fails to compile with
    /// "The type or namespace name 'Linq' does not exist in the namespace 'System'" unless these
    /// are added. Starting from the trusted platform assemblies rather than the currently loaded
    /// ones is what makes a script able to use a namespace the server has not happened to touch
    /// yet, such as <c>System.Xml.Linq</c>.
    /// </summary>
    private static readonly Lazy<PortableExecutableReference[]> RuntimeReferences =
        new(() => LoadRuntimeReferences());

    /// <summary>Simple assembly name to file path, for resolving a reference named at call time.</summary>
    private static readonly Lazy<Dictionary<string, string>> RuntimeAssemblyPaths = new(() =>
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in RuntimeReferences.Value)
        {
            // A later duplicate never wins: the first is the one the runtime itself loads.
            if (Path.GetFileNameWithoutExtension(reference.FilePath) is { Length: > 0 } named
                && reference.FilePath is { Length: > 0 } file)
                paths.TryAdd(named, file);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && assembly.GetName().Name is { Length: > 0 } name
                && assembly.Location is { Length: > 0 } location)
                paths.TryAdd(name, location);
        }

        return paths;
    });

    public async Task<ScriptResult> RunAsync(ScriptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<ScriptDiagnostic>();

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Failure(stopwatch, "Script code is empty.");
        }

        if (request.Code.Length > ScriptLimits.CodeCharacters)
        {
            return Failure(stopwatch, $"Script code exceeds {ScriptLimits.CodeCharacters} characters.");
        }

        var options = BuildOptions(request, diagnostics);
        var globals = new ScriptGlobals { Args = request.Arguments, Globals = request.Globals };
        var stdout = new BoundedTextWriter(ScriptLimits.OutputCharacters);
        var stderr = new BoundedTextWriter(ScriptLimits.OutputCharacters);

        var before = new ProcessState();
        ScriptState<object>? state = null;
        Exception? thrown = null;
        var timedOut = false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.TimeoutMs);
        try
        {
            before.Capture(request);
            // Console and the current directory are process-wide, so the run is only thread-safe as
            // long as one script runs at a time; the server handles one request at a time.
            Console.SetOut(stdout);
            Console.SetError(stderr);
            state = await Task.Run(
                () => CSharpScript.RunAsync(request.Code, options, globals, typeof(ScriptGlobals), timeout.Token),
                timeout.Token);
        }
        catch (CompilationErrorException error)
        {
            foreach (var problem in error.Diagnostics) diagnostics.Add(Describe(problem));
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
        }
        catch (Exception error)
        {
            // A script that throws surfaces here: Roslyn rethrows the script's own exception unless
            // it was asked to catch it, and it may also be the compilation machinery failing.
            thrown = error;
        }
        finally
        {
            before.Restore();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (timedOut)
        {
            // A script that never awaits cannot observe the token, so the task may still be running
            // on a thread pool thread after this returns. Cancellation is reported to the caller
            // nonetheless; the process ends the run when the session closes.
            diagnostics.Add(new ScriptDiagnostic("Error", "CSX0001", $"Script exceeded the {request.TimeoutMs} ms timeout.", 0, 0));
        }

        if (thrown is not null)
        {
            diagnostics.Add(new ScriptDiagnostic("Error", "CSX0002", Describe(thrown), 0, 0));
        }

        var truncated = stdout.Truncated || stderr.Truncated;
        var success = thrown is null && !timedOut && !diagnostics.Exists(item => item.Severity == "Error");
        return new ScriptResult(
            success,
            Value(state?.ReturnValue),
            state?.ReturnValue?.GetType().FullName,
            Variables(state, diagnostics),
            stdout.ToString(),
            stderr.ToString(),
            diagnostics.ToArray(),
            stopwatch.ElapsedMilliseconds,
            timedOut,
            truncated,
            success ? null : Summary(diagnostics, timedOut, thrown));
    }

    /// <summary>
    /// Builds the compilation options. A reference that cannot be resolved is reported as a
    /// diagnostic and skipped rather than failing the call, so one wrong name does not hide the
    /// rest of the script's problems.
    /// </summary>
    private static ScriptOptions BuildOptions(ScriptRequest request, List<ScriptDiagnostic> diagnostics)
    {
        var options = ScriptOptions.Default
            .WithReferences(RuntimeReferences.Value)
            .WithImports(ScriptLimits.DefaultImports.Concat(request.Imports))
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithOptimizationLevel(OptimizationLevel.Debug);
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            options = options.WithFilePath(Path.Combine(request.WorkingDirectory, "script.csx"));
        }

        var references = new List<MetadataReference>();
        foreach (var reference in request.References)
        {
            var resolved = Resolve(reference);
            if (resolved is null)
            {
                diagnostics.Add(new ScriptDiagnostic("Warning", "CSX0003",
                    $"Assembly reference '{reference}' was not found and was skipped.", 0, 0));
                continue;
            }

            references.Add(resolved);
        }

        return references.Count == 0 ? options : options.AddReferences(references);
    }

    /// <summary>
    /// Accepts either an absolute path to an assembly file or a simple or full assembly name, which
    /// is looked up among the assemblies already loaded in this process.
    /// </summary>
    private static PortableExecutableReference? Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (File.Exists(reference))
        {
            return MetadataReference.CreateFromFile(Path.GetFullPath(reference));
        }

        if (RuntimeAssemblyPaths.Value.TryGetValue(reference, out var path))
        {
            return MetadataReference.CreateFromFile(path);
        }

        try
        {
            var assembly = Assembly.Load(new AssemblyName(reference));
            if (assembly.Location is { Length: > 0 } location)
            {
                return MetadataReference.CreateFromFile(location);
            }
        }
        catch (Exception error) when (error is FileNotFoundException or FileLoadException or BadImageFormatException or ArgumentException)
        {
            // Reported as a warning by the caller.
        }

        return null;
    }

    private static PortableExecutableReference[] LoadRuntimeReferences()
    {
        // Deduplicated by path: the platform assembly list and the loaded assemblies overlap
        // almost completely, and a reference added twice is compiled twice.
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (var path in platform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(path)) paths.Add(path);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && assembly.Location is { Length: > 0 } location && File.Exists(location))
            {
                paths.Add(location);
            }
        }

        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static ScriptVariable[] Variables(ScriptState<object>? state, List<ScriptDiagnostic> diagnostics)
    {
        if (state is null)
        {
            return [];
        }

        var variables = new List<ScriptVariable>();
        foreach (var variable in state.Variables)
        {
            if (variables.Count == ScriptLimits.Variables)
            {
                diagnostics.Add(new ScriptDiagnostic("Information", "CSX0004",
                    $"Only the first {ScriptLimits.Variables} variables are reported.", 0, 0));
                break;
            }

            variables.Add(new ScriptVariable(variable.Name, variable.Type?.FullName ?? "object", Value(variable.Value) ?? "null"));
        }

        return variables.ToArray();
    }

    /// <summary>Renders a value for a reader, as JSON when it is JSON and as text otherwise.</summary>
    private static string? Value(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string text;
        try
        {
            text = value switch
            {
                JsonElement element => element.GetRawText(),
                string line => line,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
        }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException)
        {
            text = $"<{value.GetType().FullName}>";
        }

        return text.Length > ScriptLimits.ValueCharacters ? text[..ScriptLimits.ValueCharacters] + "…" : text;
    }

    private static string Describe(Exception error) => error switch
    {
        AggregateException aggregate when aggregate.InnerExceptions.Count > 0 => Describe(aggregate.InnerExceptions[0]),
        _ => $"{error.GetType().Name}: {error.Message}"
    };

    private static ScriptDiagnostic Describe(Diagnostic diagnostic)
    {
        var line = diagnostic.Location.GetLineSpan();
        return new ScriptDiagnostic(
            diagnostic.Severity.ToString(),
            diagnostic.Id,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            line.StartLinePosition.Line + 1,
            line.StartLinePosition.Character + 1);
    }

    private static string Summary(List<ScriptDiagnostic> diagnostics, bool timedOut, Exception? thrown)
    {
        if (timedOut)
        {
            return "Script timed out.";
        }

        if (thrown is not null)
        {
            return Describe(thrown);
        }

        var first = diagnostics.Find(item => item.Severity == "Error");
        return first is null ? "Script failed." : $"{first.Id}: {first.Message}";
    }

    private static ScriptResult Failure(Stopwatch stopwatch, string error) =>
        new(false, null, null, [], "", "", [], stopwatch.ElapsedMilliseconds, false, false, error);

    /// <summary>
    /// The process-wide state a run changes and has to put back: the current directory, the
    /// environment entries the call asked for, and the two console writers.
    /// </summary>
    private sealed class ProcessState
    {
        private string? _directory;
        private Dictionary<string, string?> _environment = [];
        private TextWriter? _out;
        private TextWriter? _error;

        public void Capture(ScriptRequest request)
        {
            _out = Console.Out;
            _error = Console.Error;
            _directory = Directory.GetCurrentDirectory();
            _environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (name, value) in request.Environment)
            {
                _environment[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory) && Directory.Exists(request.WorkingDirectory))
            {
                Directory.SetCurrentDirectory(Path.GetFullPath(request.WorkingDirectory));
            }
        }

        public void Restore()
        {
            foreach (var (name, value) in _environment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            if (_directory is not null)
            {
                Directory.SetCurrentDirectory(_directory);
            }

            if (_out is not null && _error is not null)
            {
                Console.SetOut(_out);
                Console.SetError(_error);
            }
        }
    }
}
