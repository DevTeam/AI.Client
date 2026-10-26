namespace AI.Agent.Evals;

using System.Text.Json;
using AI.Contracts.Projects;

/// <summary>Rechecks recorded CLI evidence after a checker correction, preserving the original model run.</summary>
public static class CliSetupAudit
{
    private static readonly JsonSerializerOptions Json = new(Scenario.Json) { WriteIndented = true };

    public static async Task<int> RunAsync(string runDirectory, string scenariosDirectory, string filter, CancellationToken token)
    {
        var scenarios = Scenario.Load(scenariosDirectory, filter).ToDictionary(scenario => scenario.Id);
        var reports = new List<AuditResult>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(runDirectory, "attempts"), "result.json", SearchOption.AllDirectories))
        {
            var recorded = JsonSerializer.Deserialize<AttemptResult>(await File.ReadAllTextAsync(file, token), Scenario.Json)
                ?? throw new InvalidDataException($"Missing result: {file}");
            if (!scenarios.TryGetValue(recorded.Scenario, out var scenario)) continue;
            var checks = scenario.Checks ?? [];
            if (checks.Any(check => check.Type is CheckType.Command or CheckType.FileExists or CheckType.FileAbsent
                or CheckType.FileContains or CheckType.FileNotContains or CheckType.FileUnchanged
                or CheckType.WorkspaceContains or CheckType.WorkspaceNotContains))
                throw new InvalidDataException($"{scenario.Id} needs a live workspace and cannot be audited from saved evidence.");
            var directory = Path.GetDirectoryName(file)!;
            using var chatJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "chat.json"), token));
            var chat = EvalHost.ReadChat(chatJson.RootElement) ?? throw new InvalidDataException("Missing chat.");
            var project = JsonSerializer.Deserialize<ProjectDetails>(await File.ReadAllTextAsync(Path.Combine(directory, "project.json"), token), Scenario.Json);
            var facts = new AttemptFacts(recorded.FinalAnswer, recorded.ToolCalls, recorded.ToolErrors, recorded.Prompts,
                scenario.Directory, chat.Messages.SelectMany(message => message.ToolCalls ?? [])
                    .Select(call => $"{call.Name} {call.Arguments}").ToArray(), project?.DirectoryGrants, AttemptRunner.Transcript(chat.Messages));
            var empty = Workspace.Snapshot(Path.Combine(directory, "absent-workspace"));
            var verdicts = await Checks.RunAsync(checks, empty, empty, recorded.WorkspaceChanges, facts, token);
            reports.Add(new AuditResult(recorded.Scenario, recorded.Attempt,
                recorded.Outcome == "completed" && verdicts.All(result => result.Passed), verdicts));
        }

        if (reports.Count == 0) throw new InvalidDataException("No recorded CLI attempts matched.");
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "audit-cli.json"), JsonSerializer.Serialize(reports, Json), token);
        List<string> lines = ["# CLI evidence audit", "", "Original result.json and summary files are unchanged. These verdicts use current scenario checks and paired tool results.", "",
            "| Scenario | Attempt | Passed | Failed checks |", "| --- | --- | --- | --- |"];
        lines.AddRange(reports.OrderBy(result => result.Scenario, StringComparer.Ordinal).ThenBy(result => result.Attempt)
            .Select(result => $"| {result.Scenario} | {result.Attempt} | {result.Passed} | {string.Join("; ", result.Checks.Where(check => !check.Passed).Select(check => check.Name))} |"));
        lines.Add($"\nPassed {reports.Count(result => result.Passed)} of {reports.Count}.");
        await File.WriteAllLinesAsync(Path.Combine(runDirectory, "audit-cli.md"), lines, token);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
        return reports.All(result => result.Passed) ? 0 : 1;
    }

    private sealed record AuditResult(string Scenario, int Attempt, bool Passed, IReadOnlyList<CheckResult> Checks);
}
