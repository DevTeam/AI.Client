namespace AI.Agent.Evals;

using System.Diagnostics;
using System.Globalization;

/// <summary>
/// Runs agent scenarios against a real Host and model, and compares runs. See docs/38-agent-evaluation.md.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          run      [--scenarios <dir>] [--filter <id|tag:name,...>] [--repeat <n>] [--parallel <n>]
                   [--out <dir>] [--configuration <Debug|Release>] [--no-build] [--no-route] [--connection <file>]
                   [--include-slow]   without a filter, scenarios tagged slow are skipped unless this is given
          list     [--scenarios <dir>] [--filter <...>]
          compare  <baseline-run-dir> <candidate-run-dir> [--out <file>]
          trace    <attempt-dir> [--width <n>]   a short timeline of one attempt
          summarize <run-dir>... [--out <dir>]   rebuild a summary from recorded attempts; a later run
                   replaces a scenario of an earlier one, so partial runs and reruns merge into one

        The endpoint comes from evals/connection.json (see evals/connection.example.json; the file is
        ignored by git). AI_AGENT_EVAL_BASE_URL, _MODEL, _API_KEY, _CONTEXT_WINDOW, _REASONING_EFFORT and
        _PRICES ("input,output[,cachedInput]" per million tokens) override its fields.
        """;

    public static async Task<int> Main(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        try
        {
            return args.FirstOrDefault() switch
            {
                "run" => await RunAsync(new Options(args[1..]), cancel.Token),
                "list" => List(new Options(args[1..])),
                "compare" => Compare(new Options(args[1..])),
                "trace" => Trace(new Options(args[1..])),
                "summarize" => Summarize(new Options(args[1..])),
                _ => Fail(Usage)
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or System.Text.Json.JsonException or IOException or InvalidOperationException)
        {
            return Fail(error.Message);
        }
    }

    private static async Task<int> RunAsync(Options options, CancellationToken token)
    {
        var repository = RepositoryRoot();
        var scenarios = Scenario.Load(options.Value("--scenarios") ?? Path.Combine(repository, "evals", "scenarios"), options.Value("--filter"),
            options.Has("--include-slow"));
        if (scenarios.Count == 0) return Fail("No scenario matches.");
        var model = EvalModel.Load(options.Value("--connection") ?? Path.Combine(repository, "evals", "connection.json"), !options.Has("--no-route"));
        var repeat = options.Number("--repeat", 1);
        var parallel = options.Number("--parallel", 1);
        var configuration = options.Value("--configuration") ?? "Debug";
        var hostProject = Path.Combine(repository, "src", "AI.Host", "AI.Host.csproj");
        if (!options.Has("--no-build") && await ExecuteAsync("dotnet", ["build", hostProject, "-c", configuration, "--nologo", "-v", "q"], repository, token) != 0)
            return Fail("The Host did not build.");
        var hostAssembly = Directory.EnumerateFiles(Path.Combine(repository, "src", "AI.Host", "bin", configuration), "AI.Host.dll", SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new InvalidOperationException($"AI.Host.dll is not built for {configuration}.");

        var started = DateTimeOffset.Now;
        var commit = (await CaptureAsync("git", ["rev-parse", "--short", "HEAD"], repository, token)).Trim();
        var dirty = (await CaptureAsync("git", ["status", "--porcelain"], repository, token)).Length > 0;
        // Two runs started in the same second must not share a directory.
        var name = $"{started:yyyyMMdd-HHmmss}-{commit}-{Safe(model.Model)}-{Random.Shared.Next(0x10000):x4}";
        var output = Path.Combine(options.Value("--out") ?? Path.Combine(repository, "evals", "results"), name);
        Directory.CreateDirectory(output);
        Console.WriteLine($"{scenarios.Count} scenario(s) × {repeat} on {model.Model}; results in {output}");
        // Written first, so an interrupted run can still be summarized with what it recorded.
        var header = new RunSummary(commit, dirty, model.Model, new Uri(model.BaseUrl).GetLeftPart(UriPartial.Authority), started,
            0, repeat, [], File.GetLastWriteTimeUtc(hostAssembly), !options.Has("--no-build"));
        Report.WriteHeader(output, header);

        var runner = new AttemptRunner(hostAssembly, model, new WorkspaceSource(repository));
        var work = scenarios.SelectMany(scenario => Enumerable.Range(1, repeat).Select(attempt => (scenario, attempt))).ToArray();
        var results = new List<AttemptResult>();
        var clock = Stopwatch.StartNew();
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = token }, async (item, itemToken) =>
        {
            var result = await runner.RunAsync(item.scenario, item.attempt,
                Path.Combine(output, "attempts", item.scenario.Id, item.attempt.ToString(CultureInfo.InvariantCulture)), itemToken);
            lock (results) results.Add(result);
            var failed = result.Checks.Where(check => !check.Passed).Select(check => check.Name).ToArray();
            Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Scenario} #{result.Attempt}: {result.Outcome}, "
                + $"{result.ToolCalls} tools, {result.InputTokens + result.OutputTokens} tokens, {result.DurationMs / 1000} s"
                + (failed.Length > 0 ? $"; failed: {string.Join("; ", failed)}" : "")
                + (result.Error is { } error ? $"; {error}" : ""));
        });

        var summary = header with { DurationMs = clock.ElapsedMilliseconds, Scenarios = Report.Summarize(results) };
        Report.Write(output, summary);
        Console.WriteLine();
        Console.WriteLine(Report.Markdown(summary));
        return summary.Passed == summary.Attempts ? 0 : 1;
    }

    private static int List(Options options)
    {
        var root = options.Value("--scenarios") ?? Path.Combine(RepositoryRoot(), "evals", "scenarios");
        foreach (var scenario in Scenario.Load(root, options.Value("--filter"), includeSlow: true))
            Console.WriteLine($"{scenario.Id,-32} [{string.Join(", ", scenario.Tags ?? [])}] {scenario.Description}");
        return 0;
    }

    private static int Compare(Options options)
    {
        if (options.Positional.Count != 2) return Fail(Usage);
        var text = Report.Compare(Report.Read(options.Positional[0]), Report.Read(options.Positional[1]));
        if (options.Value("--out") is { } file) File.WriteAllText(file, text);
        Console.WriteLine(text);
        return 0;
    }

    private static int Summarize(Options options)
    {
        if (options.Positional.Count == 0) return Fail(Usage);
        var summary = Report.Merge(options.Positional);
        var output = options.Value("--out") ?? options.Positional[0];
        Directory.CreateDirectory(output);
        Report.Write(output, summary);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(Report.Markdown(summary));
        return 0;
    }

    private static int Trace(Options options)
    {
        if (options.Positional.Count != 1) return Fail(Usage);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(TraceReport.Write(options.Positional[0], options.Number("--width", 200)));
        return 0;
    }

    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AI.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Run inside the AI.Client repository: AI.slnx was not found.");
    }

    private static async Task<int> ExecuteAsync(string executable, string[] arguments, string directory, CancellationToken token)
    {
        using var process = Process.Start(Start(executable, arguments, directory, false))!;
        await process.WaitForExitAsync(token);
        return process.ExitCode;
    }

    private static async Task<string> CaptureAsync(string executable, string[] arguments, string directory, CancellationToken token)
    {
        try
        {
            using var process = Process.Start(Start(executable, arguments, directory, true))!;
            var output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            return process.ExitCode == 0 ? output : "";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }

    private static ProcessStartInfo Start(string executable, string[] arguments, string directory, bool capture)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = capture };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static string Safe(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_'));

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string?> _named = new(StringComparer.Ordinal);

        public Options(string[] args)
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal)) Positional.Add(args[index]);
                else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) _named[args[index]] = args[++index];
                else _named[args[index]] = null;
            }
        }

        public List<string> Positional { get; } = [];

        public bool Has(string name) => _named.ContainsKey(name);

        public string? Value(string name) => _named.GetValueOrDefault(name);

        public int Number(string name, int fallback) => Value(name) is { } value
            ? int.Parse(value, CultureInfo.InvariantCulture) is var number and > 0 ? number : throw new ArgumentException($"{name} must be positive.")
            : fallback;
    }
}
