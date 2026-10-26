namespace AI.Agent.Evals;

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using AI.Contracts.Projects;

/// <summary>Deterministic verdicts on an attempt: what is on disk, what the program does, what was answered.</summary>
/// <summary>What the checks need to know about the attempt beside the workspace.</summary>
/// <param name="ScenarioDirectory">Replaces <c>{scenario}</c> in a command's arguments, for files the model must not see.</param>
/// <param name="Calls">Every tool call of the attempt as "name arguments".</param>
public sealed record AttemptFacts(string? Answer, int ToolCalls, int ToolErrors, int Prompts, string ScenarioDirectory,
    IReadOnlyList<string>? Calls = null, IReadOnlyList<DirectoryGrantSettings>? ProjectGrants = null,
    IReadOnlyList<string>? ToolTranscript = null);

public static class Checks
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    public static async Task<IReadOnlyList<CheckResult>> RunAsync(IReadOnlyList<Check> checks, Workspace before, Workspace after,
        IReadOnlyList<string> changes, AttemptFacts facts, CancellationToken token)
    {
        var results = new List<CheckResult>();
        foreach (var check in checks)
        {
            CheckResult result;
            try
            {
                result = await RunAsync(check, before, after, changes, facts, token);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or RegexMatchTimeoutException or System.ComponentModel.Win32Exception)
            {
                result = new CheckResult(Name(check), false, error.Message);
            }

            results.Add(result);
        }

        return results;
    }

    private static async Task<CheckResult> RunAsync(Check check, Workspace before, Workspace after, IReadOnlyList<string> changes,
        AttemptFacts facts, CancellationToken token)
    {
        var answer = facts.Answer;
        var toolCalls = facts.ToolCalls;
        var toolErrors = facts.ToolErrors;
        var prompts = facts.Prompts;
        var name = Name(check);
        switch (check.Type)
        {
            case CheckType.Command:
                return await RunCommandAsync(check, after.Root, facts.ScenarioDirectory, name, token);
            case CheckType.FileExists:
                return new CheckResult(name, File.Exists(PathOf(after, check)));
            case CheckType.FileAbsent:
                return new CheckResult(name, !File.Exists(PathOf(after, check)));
            case CheckType.FileContains or CheckType.FileNotContains:
            {
                var file = PathOf(after, check);
                if (!File.Exists(file)) return new CheckResult(name, false, "The file does not exist.");
                var found = Match(await File.ReadAllTextAsync(file, token), check);
                return new CheckResult(name, found == (check.Type == CheckType.FileContains));
            }
            case CheckType.FileUnchanged:
                return new CheckResult(name, after.SameAs(before, Required(check.Path, check)));
            case CheckType.AnswerMatches or CheckType.AnswerNotMatches:
            {
                var found = answer is not null && Match(answer, check);
                return new CheckResult(name, found == (check.Type == CheckType.AnswerMatches), answer is null ? "There is no final answer." : null);
            }
            case CheckType.NoWorkspaceChanges:
                return new CheckResult(name, changes.Count == 0, changes.Count == 0 ? null : string.Join(", ", changes));
            case CheckType.OnlyChanged:
            {
                var allowed = check.Paths ?? [Required(check.Path, check)];
                var outside = changes.Where(change => !allowed.Any(path => Under(change[2..], path))).ToArray();
                return new CheckResult(name, outside.Length == 0, outside.Length == 0 ? null : string.Join(", ", outside));
            }
            case CheckType.WorkspaceContains or CheckType.WorkspaceNotContains:
            {
                var matches = new List<string>();
                foreach (var path in after.Files.Where(path => check.Paths is null || check.Paths.Any(prefix => Under(path, prefix))))
                {
                    var file = new FileInfo(Path.Combine(after.Root, path));
                    if (file.Length > 2_000_000) continue;
                    var text = await File.ReadAllTextAsync(file.FullName, token);
                    if (!text.Contains('\0', StringComparison.Ordinal) && Match(text, check)) matches.Add(path);
                }

                return check.Type == CheckType.WorkspaceContains
                    ? new CheckResult(name, matches.Count > 0)
                    : new CheckResult(name, matches.Count == 0, matches.Count == 0 ? null : string.Join(", ", matches.Take(10)));
            }
            case CheckType.MaxToolCalls:
                return new CheckResult(name, toolCalls <= check.Value, $"{toolCalls} calls");
            case CheckType.MaxToolErrors:
                return new CheckResult(name, toolErrors <= check.Value, $"{toolErrors} failed calls");
            case CheckType.CallsMatch or CheckType.CallsNotMatch:
            {
                var matching = (facts.Calls ?? []).Where(call => Match(call, check)).ToArray();
                return check.Type == CheckType.CallsMatch
                    ? new CheckResult(name, matching.Length > 0)
                    : new CheckResult(name, matching.Length == 0, matching.Length == 0 ? null
                        : string.Join("; ", matching.Take(3).Select(call => call.Length > 200 ? call[..200] + "…" : call)));
            }
            case CheckType.MinPrompts:
                return new CheckResult(name, prompts >= check.Value, $"{prompts} questions");
            case CheckType.MaxPrompts:
                return new CheckResult(name, prompts <= check.Value, $"{prompts} questions");
            case CheckType.ProjectGrantCount:
                return new CheckResult(name, facts.ProjectGrants?.Count == check.Value, $"{facts.ProjectGrants?.Count} stored grants");
            case CheckType.ProjectGrantMatches:
                return new CheckResult(name, facts.ProjectGrants?.Any(grant => Match(JsonSerializer.Serialize(grant, Scenario.Json), check)) == true);
            case CheckType.ToolTranscriptMatches:
                return new CheckResult(name, Match(string.Join('\n', facts.ToolTranscript ?? []), check));
            case CheckType.MaxMatchingCalls:
            {
                var count = (facts.Calls ?? []).Count(call => Match(call, check));
                return new CheckResult(name, count <= check.Value, $"{count} matching calls");
            }
            default:
                throw new ArgumentException($"Unknown check type {check.Type}.");
        }
    }

    private static async Task<CheckResult> RunCommandAsync(Check check, string workspace, string scenario, string name, CancellationToken token)
    {
        var start = new ProcessStartInfo(Required(check.Executable, check))
        {
            WorkingDirectory = check.Path is null ? workspace : Path.Combine(workspace, check.Path),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var argument in check.Arguments ?? []) start.ArgumentList.Add(argument.Replace("{scenario}", scenario, StringComparison.Ordinal));
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{check.Executable} did not start.");
        // A program that reads its input must not wait for a person: it gets the scenario's input, then the end.
        await process.StandardInput.WriteAsync(check.Input ?? "");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(check.TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new CheckResult(name, false, $"Timed out after {check.TimeoutSeconds} s.");
        }

        var text = (await output) + (await error);
        var printed = check.OutputPattern is not { } pattern
            || Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);
        var passed = process.ExitCode == check.ExitCode && printed;
        return new CheckResult(name, passed, passed ? null
            : $"Exit code {process.ExitCode}{(printed ? "" : $", output does not match /{check.OutputPattern}/")}. {Tail(text, 1500)}");
    }

    private static string Name(Check check) => check.Name ?? check.Type switch
    {
        CheckType.Command => $"{check.Executable} {string.Join(' ', check.Arguments ?? [])}".Trim(),
        CheckType.AnswerMatches or CheckType.AnswerNotMatches => $"{check.Type} /{check.Pattern}/",
        CheckType.FileContains or CheckType.FileNotContains => $"{check.Type} {check.Path} /{check.Pattern}/",
        CheckType.WorkspaceContains or CheckType.WorkspaceNotContains or CheckType.CallsMatch or CheckType.CallsNotMatch
            => $"{check.Type} /{check.Pattern}/",
        CheckType.MaxToolCalls or CheckType.MaxToolErrors or CheckType.MinPrompts or CheckType.MaxPrompts => $"{check.Type} {check.Value}",
        CheckType.OnlyChanged => $"{check.Type} {string.Join(", ", check.Paths ?? [check.Path ?? ""])}",
        _ => $"{check.Type} {check.Path}".Trim()
    };

    private static bool Match(string text, Check check) =>
        Regex.IsMatch(text, Required(check.Pattern, check), RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);

    private static string PathOf(Workspace workspace, Check check) => Path.Combine(workspace.Root, Required(check.Path, check));

    private static bool Under(string file, string path) =>
        string.Equals(file, path.TrimEnd('/'), StringComparison.Ordinal) || file.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal);

    private static string Required(string? value, Check check) =>
        value ?? throw new ArgumentException($"The {check.Type} check is missing a required field.");

    private static string Tail(string text, int length) => text.Length <= length ? text.Trim() : "…" + text[^length..].Trim();
}
