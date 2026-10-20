namespace AI.Agent.Evals;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>What a whole run measured, per scenario over its attempts; <c>summary.json</c> is what <c>compare</c> reads.</summary>
/// <param name="HostRebuilt">
/// False for <c>--no-build</c>: the Host was not built from <paramref name="Commit"/> and may be older,
/// so <paramref name="HostBuiltAt"/> is what says which code ran.
/// </param>
public sealed record RunSummary(
    string Commit,
    bool Dirty,
    string Model,
    string Endpoint,
    DateTimeOffset StartedAt,
    long DurationMs,
    int Repeat,
    IReadOnlyList<ScenarioSummary> Scenarios,
    DateTimeOffset? HostBuiltAt = null,
    bool HostRebuilt = true)
{
    public int Attempts => Scenarios.Sum(item => item.Attempts);
    public int Passed => Scenarios.Sum(item => item.Passed);
}

/// <param name="PassAll">Whether every attempt passed: pass^k, which says more about reliability than the rate.</param>
public sealed record ScenarioSummary(
    string Scenario,
    int Attempts,
    int Passed,
    bool PassAll,
    IReadOnlyDictionary<string, int> Outcomes,
    double MeanDurationMs,
    double MeanToolCalls,
    double MeanToolErrors,
    double MeanNonZeroExits,
    double MeanCompactions,
    double MeanRepeatedToolCalls,
    double MeanModelSteps,
    double MeanInputTokens,
    double MeanOutputTokens,
    double CacheHitRate,
    decimal? MeanCost,
    IReadOnlyList<string> FailedChecks)
{
    public double PassRate => Attempts == 0 ? 0 : (double)Passed / Attempts;

    public static ScenarioSummary From(string scenario, IReadOnlyList<AttemptResult> attempts)
    {
        var input = attempts.Sum(item => item.InputTokens);
        var costs = attempts.Where(item => item.Cost is not null).Select(item => item.Cost!.Value).ToArray();
        return new ScenarioSummary(scenario, attempts.Count, attempts.Count(item => item.Passed), attempts.All(item => item.Passed),
            attempts.GroupBy(item => item.Outcome).ToDictionary(group => group.Key, group => group.Count()),
            attempts.Average(item => item.DurationMs), attempts.Average(item => item.ToolCalls), attempts.Average(item => item.ToolErrors),
            attempts.Average(item => item.NonZeroExits), attempts.Average(item => item.Compactions),
            attempts.Average(item => item.RepeatedToolCalls), attempts.Average(item => item.ModelSteps),
            attempts.Average(item => item.InputTokens), attempts.Average(item => item.OutputTokens),
            input == 0 ? 0 : (double)attempts.Sum(item => item.CachedInputTokens) / input,
            costs.Length == 0 ? null : costs.Average(),
            attempts.SelectMany(item => item.Checks.Where(check => !check.Passed).Select(check => check.Name)
                    .Concat(item.Outcome == "completed" ? [] : [$"outcome: {item.Outcome}"]))
                .GroupBy(name => name).Select(group => $"{group.Key} ×{group.Count()}").ToArray());
    }
}

public static class Report
{
    private static readonly JsonSerializerOptions Indented = new(Scenario.Json) { WriteIndented = true };

    public static void Write(string directory, RunSummary summary)
    {
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, Indented));
        File.WriteAllText(Path.Combine(directory, "summary.md"), Markdown(summary));
    }

    public static void WriteHeader(string directory, RunSummary header) =>
        File.WriteAllText(Path.Combine(directory, "run.json"), JsonSerializer.Serialize(header, Indented));

    public static IReadOnlyList<ScenarioSummary> Summarize(IEnumerable<AttemptResult> results) =>
        results.GroupBy(item => item.Scenario).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => ScenarioSummary.From(group.Key, group.OrderBy(item => item.Attempt).ToArray())).ToArray();

    /// <summary>
    /// One summary from the attempts recorded in <paramref name="directories"/>. A scenario takes all
    /// its attempts from the last directory that has any, so a rerun replaces what it reran; the
    /// description of the run comes from the first.
    /// </summary>
    public static RunSummary Merge(IReadOnlyList<string> directories)
    {
        var attempts = new Dictionary<string, IReadOnlyList<AttemptResult>>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var root = Path.Combine(directory, "attempts");
            if (!Directory.Exists(root)) throw new InvalidDataException($"{directory} has no attempts.");
            foreach (var scenario in Directory.EnumerateDirectories(root))
            {
                var results = Directory.EnumerateFiles(scenario, "result.json", SearchOption.AllDirectories)
                    .Select(file => JsonSerializer.Deserialize<AttemptResult>(File.ReadAllText(file), Scenario.Json)!)
                    .ToArray();
                if (results.Length > 0) attempts[Path.GetFileName(scenario)] = results;
            }
        }

        var header = Header(directories[0]);
        return header with
        {
            Scenarios = Summarize(attempts.Values.SelectMany(item => item)),
            DurationMs = attempts.Values.SelectMany(item => item).Sum(item => item.DurationMs),
            Repeat = attempts.Values.Select(item => item.Count).DefaultIfEmpty(0).Max()
        };
    }

    // Runs recorded before run.json existed are described by their directory name: time-commit-model.
    private static RunSummary Header(string directory)
    {
        foreach (var name in new[] { "run.json", "summary.json" })
        {
            var file = Path.Combine(directory, name);
            if (File.Exists(file)) return JsonSerializer.Deserialize<RunSummary>(File.ReadAllText(file), Scenario.Json)!;
        }

        var parts = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)).Split('-', 4);
        var started = parts.Length >= 2 && DateTime.TryParseExact(parts[0] + parts[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var time) ? new DateTimeOffset(time) : DateTimeOffset.MinValue;
        return new RunSummary(parts.Length >= 3 ? parts[2] : "?", false, parts.Length >= 4 ? parts[3] : "?", "?", started, 0, 0, []);
    }

    public static RunSummary Read(string directory) =>
        JsonSerializer.Deserialize<RunSummary>(File.ReadAllText(Path.Combine(directory, "summary.json")), Scenario.Json)
        ?? throw new InvalidDataException($"{directory} has no summary.");

    public static string Markdown(RunSummary summary)
    {
        var text = new StringBuilder();
        text.AppendLine(Invariant($"# Agent evaluation {summary.StartedAt:yyyy-MM-dd HH:mm}"));
        text.AppendLine();
        text.AppendLine(Invariant($"Commit `{summary.Commit}`{(summary.Dirty ? " with uncommitted changes" : "")}, model `{summary.Model}` at {summary.Endpoint}, {summary.Repeat} attempt(s) per scenario, {summary.DurationMs / 1000} s."));
        if (!summary.HostRebuilt)
        {
            text.AppendLine();
            text.AppendLine(Invariant($"The Host was not rebuilt for this run (`--no-build`); it was built {summary.HostBuiltAt:yyyy-MM-dd HH:mm} UTC."));
        }
        text.AppendLine();
        text.AppendLine(Invariant($"**Passed {summary.Passed} of {summary.Attempts}**"));
        text.AppendLine();
        text.AppendLine("| Scenario | Pass | Tools | Errors | Exit≠0 | Repeats | Compactions | Steps | Input | Output | Cache | Cost | Time, s |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var item in summary.Scenarios)
            text.AppendLine(Invariant($"| {item.Scenario} | {item.Passed}/{item.Attempts} | {item.MeanToolCalls:0.#} | {item.MeanToolErrors:0.#} | {item.MeanNonZeroExits:0.#} | {item.MeanRepeatedToolCalls:0.#} | {item.MeanCompactions:0.#} | {item.MeanModelSteps:0.#} | {item.MeanInputTokens:0} | {item.MeanOutputTokens:0} | {item.CacheHitRate:P0} | {Money(item.MeanCost)} | {item.MeanDurationMs / 1000:0} |"));
        var failures = summary.Scenarios.Where(item => item.FailedChecks.Count > 0).ToArray();
        if (failures.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("## Failures");
            foreach (var item in failures)
                text.AppendLine(Invariant($"- **{item.Scenario}**: {string.Join("; ", item.FailedChecks)}"));
        }

        return text.ToString();
    }

    /// <summary>Scenario by scenario, what changed from <paramref name="baseline"/> to <paramref name="candidate"/>.</summary>
    public static string Compare(RunSummary baseline, RunSummary candidate)
    {
        var text = new StringBuilder();
        text.AppendLine(Invariant($"# `{baseline.Commit}` → `{candidate.Commit}`{(candidate.Dirty ? " (uncommitted)" : "")}"));
        text.AppendLine();
        text.AppendLine(Invariant($"Models: `{baseline.Model}` → `{candidate.Model}`. Passed {baseline.Passed}/{baseline.Attempts} → {candidate.Passed}/{candidate.Attempts}."));
        text.AppendLine();
        text.AppendLine("| Scenario | Pass rate | Tools | Errors | Input | Output | Cost | Time, s |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        var before = baseline.Scenarios.ToDictionary(item => item.Scenario);
        var after = candidate.Scenarios.ToDictionary(item => item.Scenario);
        foreach (var name in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(name, out var a);
            after.TryGetValue(name, out var b);
            if (a is null || b is null)
            {
                text.AppendLine(Invariant($"| {name} | {(a is null ? "new" : "removed")} | | | | | | |"));
                continue;
            }

            var flag = b.PassRate < a.PassRate ? " ⚠" : b.PassRate > a.PassRate ? " ✓" : "";
            text.AppendLine(Invariant($"| {name} | {a.PassRate:P0} → {b.PassRate:P0}{flag} | {Delta(a.MeanToolCalls, b.MeanToolCalls)} | {Delta(a.MeanToolErrors, b.MeanToolErrors)} | {Delta(a.MeanInputTokens, b.MeanInputTokens)} | {Delta(a.MeanOutputTokens, b.MeanOutputTokens)} | {Money(a.MeanCost)} → {Money(b.MeanCost)} | {Delta(a.MeanDurationMs / 1000, b.MeanDurationMs / 1000)} |"));
        }

        text.AppendLine();
        text.AppendLine("With few attempts a pass rate moves by chance; repeat a scenario before trusting a single flip.");
        return text.ToString();
    }

    private static string Delta(double a, double b) =>
        Invariant($"{a:0.#} → {b:0.#}") + (a == 0 ? "" : Invariant($" ({(b - a) / a:+0%;-0%;0%})"));

    private static string Money(decimal? value) => value is { } cost ? cost.ToString("0.####", CultureInfo.InvariantCulture) : "—";

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
