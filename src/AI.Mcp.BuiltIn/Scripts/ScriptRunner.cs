namespace AI.Mcp.BuiltIn.Scripts;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Contracts.FileSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

/// <summary>
/// Runs one C# script in the disposable worker process with Roslyn scripting. Everything the run needs — code,
/// imports, assembly references, arguments, globals, working directory, environment and a timeout —
/// arrives in a single <see cref="ScriptRequest"/>, and everything it produced comes back in a
/// single <see cref="ScriptResult"/>.
///
/// A script can reach whatever the worker process can, so the boundary is the worker's: the
/// session's sandbox when the Host switched it on, the user's account otherwise. The working
/// directory and the environment handed to the script are a convenience for the code being
/// written, not a boundary.
/// </summary>
public sealed class ScriptRunner(IFileSystem files, IPath paths) : IScriptRunner
{
    /// <summary>Guards the two process-wide caches below; composition access is serialized.</summary>
    private static readonly object Gate = new();

    /// <summary>
    /// Every assembly of the runtime the server itself runs on, resolved once per process. Roslyn's
    /// <see cref="ScriptOptions.Default"/> references almost nothing — a script written with the
    /// default imports (<c>System.Linq</c>, <c>System.Text.Json</c>, …) fails to compile with
    /// "The type or namespace name 'Linq' does not exist in the namespace 'System'" unless these
    /// are added. Starting from the trusted platform assemblies rather than the currently loaded
    /// ones is what makes a script able to use a namespace the server has not happened to touch
    /// yet, such as <c>System.Xml.Linq</c>.
    /// </summary>
    /// <remarks>
    /// The set belongs to the process, not to one run, so it is computed through the contract once
    /// and then shared: building it per run would re-read every platform assembly for every script.
    /// </remarks>
    private static Task<PortableExecutableReference[]>? _runtimeReferences;

    /// <summary>Simple assembly name to file path, for resolving a reference named at call time.</summary>
    private static Task<IReadOnlyDictionary<string, string>>? _runtimeAssemblyPaths;

    private readonly IFileSystem _files = files;
    private readonly IPath _paths = paths;

    /// <summary>
    /// Compiles and runs a trivial script with the default options, which loads the references and
    /// gets the compiler's code compiled by the JIT. Most of a cold run's time goes there; what is left
    /// for the real script is its own compilation.
    /// </summary>
    public async Task WarmUpAsync()
    {
        var options = ScriptOptions.Default
            .WithReferences(await RuntimeReferencesAsync())
            .WithImports(ScriptLimits.DefaultImports)
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithOptimizationLevel(OptimizationLevel.Debug);
        await CSharpScript.RunAsync("var warm = Args.Length; warm", options, new ScriptGlobals(), typeof(ScriptGlobals));
    }

    public async Task<ScriptResult> RunAsync(ScriptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<ScriptDiagnostic>();

        if (request.TimeoutMs is < 1 or > ScriptLimits.MaxTimeoutMs)
        {
            return Failure(stopwatch, $"Timeout must be between 1 and {ScriptLimits.MaxTimeoutMs} milliseconds.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Failure(stopwatch, "Script code is empty.");
        }

        if (request.Code.Length > ScriptLimits.CodeCharacters)
        {
            return Failure(stopwatch, $"Script code exceeds {ScriptLimits.CodeCharacters} characters.");
        }

        var options = await BuildOptionsAsync(request, diagnostics);
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
            await before.CaptureAsync(request, _files, _paths);
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
            // Cooperative cancellation can miss blocking script code. The parent process enforces
            // the deadline by terminating this disposable worker when necessary.
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
    private async Task<ScriptOptions> BuildOptionsAsync(ScriptRequest request, List<ScriptDiagnostic> diagnostics)
    {
        var options = ScriptOptions.Default
            .WithReferences(await RuntimeReferencesAsync())
            .WithImports(ScriptLimits.DefaultImports.Concat(request.Imports))
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithOptimizationLevel(OptimizationLevel.Debug);
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            options = options.WithFilePath(_paths.Combine(request.WorkingDirectory, "script.csx"));
        }

        var references = new List<MetadataReference>();
        foreach (var reference in request.References)
        {
            var resolved = await ResolveAsync(reference);
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
    private async Task<PortableExecutableReference?> ResolveAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (await _files.FileExistsAsync(reference, CancellationToken.None))
        {
            return MetadataReference.CreateFromFile(_paths.GetFullPath(reference));
        }

        if ((await RuntimeAssemblyPathsAsync()).TryGetValue(reference, out var path))
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

    private Task<PortableExecutableReference[]> RuntimeReferencesAsync() =>
        Cache(ref _runtimeReferences, LoadRuntimeReferencesAsync);

    private Task<IReadOnlyDictionary<string, string>> RuntimeAssemblyPathsAsync() =>
        Cache(ref _runtimeAssemblyPaths,
            async () =>
            {
                var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var reference in await LoadRuntimeReferencesAsync())
                {
                    // A later duplicate never wins: the first is the one the runtime itself loads.
                    if (reference.FilePath is { Length: > 0 } file
                        && _paths.GetFileNameWithoutExtension(file) is { Length: > 0 } named)
                        paths.TryAdd(named, file);
                }

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.IsDynamic && assembly.GetName().Name is { Length: > 0 } name
                        && assembly.Location is { Length: > 0 } location)
                        paths.TryAdd(name, location);
                }

                return (IReadOnlyDictionary<string, string>)paths;
            });

    /// <summary>
    /// Answers from the process-wide cache, starting the computation once when it has not run yet.
    /// The two caches describe the running process rather than one run, so they are kept across
    /// instances and guarded by <see cref="Gate"/>; the composition serializes access to this runner
    /// anyway. A cached load is not tied to a caller's token, because one cancelled run must not
    /// leave the process with a cache that failed forever.
    /// </summary>
    private static Task<T> Cache<T>(ref Task<T>? slot, Func<Task<T>> compute)
        where T : class
    {
        lock (Gate)
        {
            return slot ??= compute();
        }
    }

    private async Task<PortableExecutableReference[]> LoadRuntimeReferencesAsync()
    {
        // Deduplicated by path: the platform assembly list and the loaded assemblies overlap
        // almost completely, and a reference added twice is compiled twice.
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (var path in platform.Split(_paths.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (await _files.FileExistsAsync(path, CancellationToken.None)) paths.Add(path);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && assembly.Location is { Length: > 0 } location
                && await _files.FileExistsAsync(location, CancellationToken.None))
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

    /// <summary>
    /// Renders a value for a reader: a scalar as invariant text, and anything else (a collection, a
    /// record, an anonymous object, a tuple) as compact JSON. An object's own <c>ToString</c> is
    /// usually its type name, which made a model run the script again just to serialize what it
    /// had already computed.
    /// </summary>
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
                IConvertible or IFormattable => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
                _ => Json(value)
            };
        }
        // Serializing reads the script's own properties, and a getter may throw anything: the value
        // then falls back to its text rather than failing the run that produced it.
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            text = Convert.ToString(value, CultureInfo.InvariantCulture) is { Length: > 0 } fallback ? fallback : $"<{value.GetType().FullName}>";
        }

        return text.Length > ScriptLimits.ValueCharacters ? text[..ScriptLimits.ValueCharacters] + "…" : text;
    }

    private static readonly JsonSerializerOptions ValueJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        IncludeFields = true,
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// The value as JSON, written only as far as a reader will be shown: a variable holding a million
    /// records is cut off while it is serialized rather than after all of it was.
    /// </summary>
    private static string Json(object value)
    {
        using var buffer = new BoundedStream(ScriptLimits.ValueCharacters * 4 + 4);
        try
        {
            JsonSerializer.Serialize(buffer, value, value.GetType(), ValueJson);
        }
        catch (IOException) when (buffer.Full)
        {
            // What was written so far is what is shown; the cut is marked by the caller.
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>A memory stream that refuses to grow past its limit.</summary>
    private sealed class BoundedStream(int limit) : MemoryStream
    {
        // The array overload does the work: MemoryStream's span overload calls it in a derived type.
        public override void Write(byte[] buffer, int offset, int count)
        {
            var room = Math.Max(0, limit - (int)Length);
            base.Write(buffer, offset, Math.Min(room, count));
            if (count <= room) return;
            Full = true;
            throw new IOException("The value is longer than a reader is shown.");
        }

        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override void WriteByte(byte value) => Write([value], 0, 1);

        /// <summary>Whether a write was refused; the refusal is the only way to stop the serializer early.</summary>
        public bool Full { get; private set; }
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
    /// environment entries the call asked for, and the two console writers. The current directory
    /// itself keeps its platform call — a C# script that does not inherit the process's own working
    /// directory is not scriptable, and that is one of the ADR's documented exceptions.
    /// </summary>
    private sealed class ProcessState
    {
        private string? _directory;
        private Dictionary<string, string?> _environment = [];
        private TextWriter? _out;
        private TextWriter? _error;

        public async Task CaptureAsync(ScriptRequest request, IFileSystem files, IPath paths)
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

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)
                && await files.DirectoryExistsAsync(request.WorkingDirectory, CancellationToken.None))
            {
                Directory.SetCurrentDirectory(paths.GetFullPath(request.WorkingDirectory));
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
