namespace AI.Agent.Evals.Tests;

using AI.Agent.Evals;
using Shouldly;
using Xunit;

/// <summary>
/// The two <c>trigger_wait</c> scenarios check fields of a tool result, not of a file, so a broken
/// pattern would pass for the wrong reason or fail for no reason. These tests pin each pattern to a
/// transcript shaped exactly like <see cref="AttemptRunner.Transcript"/> produces, and check that the
/// verdict flips when the evidence the pattern asks for is absent.
/// </summary>
public sealed class TriggerWaitChecksTests
{
    private const string WriterCall =
        "mcp_built_in__process_run {\"executable\":\"cmd.exe\",\"arguments\":[\"/d\",\"/c\",\"writer.cmd\"],\"waitTimeoutMs\":100}";

    private static string Call(string name, string arguments, string result) => $"CALL {name} {arguments}\nRESULT {result}";

    private static string IdleCall(bool depth) =>
        "mcp_built_in__trigger_wait {\"conditions\":[{\"type\":\"directory_idle\",\"path\":\"C:\\\\tmp\\\\out\",\"quietMs\":1200"
        + (depth ? ",\"depth\":2" : "") + "}]}";

    private static string Observation(int entries, int depth, long idleMs) =>
        "{\"conditionIndex\":0,\"type\":\"directory_idle\",\"path\":\"C:\\\\tmp\\\\out\",\"exists\":true,"
        + ",\"entries\":" + entries + ",\"depth\":" + depth + ",\"idleMs\":" + idleMs + "}";

    private static string Triggered(int entries, int depth, long idleMs) =>
        "{\"structuredContent\":{\"outcome\":\"triggered\",\"conditionIndex\":0,\"type\":\"directory_idle\","
        + "\"elapsedMs\":" + idleMs + ",\"observed\":[" + Observation(entries, depth, idleMs) + "]},\"isError\":false}";

    private static string TimedOut(int entries, int depth, long idleMs) =>
        "{\"structuredContent\":{\"outcome\":\"timeout\",\"elapsedMs\":6000,"
        + "\"observed\":[" + Observation(entries, depth, idleMs) + "]},\"isError\":false}";

    private const string RunningResult = "{\"structuredContent\":{\"status\":\"running\",\"processId\":\"handle\"}}";

    internal static IReadOnlyList<Check> ChecksOf(string scenarioId)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "evals", "scenarios")))
            directory = directory.Parent;
        directory.ShouldNotBeNull("evals/scenarios was not found above the test assembly.");
        return Scenario.Load(Path.Combine(directory.FullName, "evals", "scenarios"), scenarioId)
            .ShouldHaveSingleItem().Checks!;
    }

    private static async Task<IReadOnlyDictionary<string, CheckResult>> RunAsync(string scenarioId,
        IReadOnlyList<string> transcript, string answer, string? directory = null)
    {
        var scratch = directory ?? Directory.CreateTempSubdirectory("trigger-wait-checks-").FullName;
        var workspace = Workspace.Snapshot(scratch);
        // Checks treat a transcript entry as "CALL name arguments" and a call as "name arguments".
        var calls = transcript.Select(entry => entry.StartsWith("CALL ", StringComparison.Ordinal)
            ? entry[5..entry.IndexOf('\n')] : entry).ToArray();
        var facts = new AttemptFacts(answer, calls.Length, 0, 0, scratch, Calls: calls, ToolTranscript: transcript);
        var results = await Checks.RunAsync(ChecksOf(scenarioId), workspace, workspace, [], facts,
            TestContext.Current.CancellationToken);
        return results.ToDictionary(result => result.Name, StringComparer.Ordinal);
    }

    [Fact]
    public async Task DepthScenarioPassesOnlyWhenOneLevelCountsOneEntryAndTwoLevelsNeverSettle()
    {
        var scratch = Directory.CreateTempSubdirectory("trigger-wait-depth-scenario-");
        try
        {
            File.WriteAllText(Path.Combine(scratch.FullName, "writer.cmd"), "@echo off\n");
            File.WriteAllText(Path.Combine(scratch.FullName, "writer.ps1"), "$null = 1\n");
            var good = new[]
            {
                Call("mcp_built_in__process_run", WriterCall[WriterCall.IndexOf(' ')..], RunningResult),
                Call("mcp_built_in__trigger_wait", IdleCall(depth: false)[IdleCall(false).IndexOf(' ')..], Triggered(1, 1, 1310)),
                Call("mcp_built_in__trigger_wait", IdleCall(depth: true)[IdleCall(true).IndexOf(' ')..], TimedOut(2, 2, 410))
            };

            var passed = await RunAsync("trigger-wait-directory-idle-depth", good,
                "The one-level wait counted 1 entry at depth 1; the two-level wait saw 2 entries at depth 2 and reported idleMs 410.",
                scratch.FullName);
            passed.Values.Where(result => !result.Passed).ShouldBeEmpty();

            // A one-level count that reports two entries is not a one-level count.
            var deeper = good.ToArray();
            deeper[1] = Call("mcp_built_in__trigger_wait", IdleCall(depth: false)[IdleCall(false).IndexOf(' ')..], Triggered(2, 1, 1310));
            (await RunAsync("trigger-wait-directory-idle-depth", deeper, "nothing", scratch.FullName))
                .Where(pair => pair.Key.Contains("one level", StringComparison.OrdinalIgnoreCase))
                .ShouldHaveSingleItem().Value.Passed.ShouldBeFalse();

            // A two-level wait that fired proves the log two levels down was not being watched.
            var firedDeep = good.ToArray();
            firedDeep[2] = Call("mcp_built_in__trigger_wait", IdleCall(depth: true)[IdleCall(true).IndexOf(' ')..], Triggered(2, 2, 1310));
            (await RunAsync("trigger-wait-directory-idle-depth", firedDeep, "nothing", scratch.FullName))
                .Where(pair => pair.Key.Contains("two levels", StringComparison.OrdinalIgnoreCase))
                .ShouldHaveSingleItem().Value.Passed.ShouldBeFalse();
        }
        finally { scratch.Delete(recursive: true); }
    }

    [Fact]
    public async Task TimeoutScenarioPassesOnlyWhenTheTimedOutObservationReportsItsDiagnostics()
    {
        var scratch = Directory.CreateTempSubdirectory("trigger-wait-timeout-scenario-");
        try
        {
            File.WriteAllText(Path.Combine(scratch.FullName, "writer.cmd"), "@echo off\n");
            File.WriteAllText(Path.Combine(scratch.FullName, "writer.ps1"), "$null = 1\n");
            var good = new[]
            {
                Call("mcp_built_in__process_run", WriterCall[WriterCall.IndexOf(' ')..], RunningResult),
                Call("mcp_built_in__trigger_wait", IdleCall(depth: true)[IdleCall(true).IndexOf(' ')..], TimedOut(2, 2, 410))
            };
            var passed = await RunAsync("trigger-wait-idle-timeout-diagnostics", good,
                "The wait timed out while the writer was active; it had been quiet for 410 ms at depth 2.", scratch.FullName);
            passed.Values.Where(result => !result.Passed).ShouldBeEmpty();

            // Without idleMs a timeout says nothing about how close the silence had come.
            var noIdle = new[]
            {
                good[0],
                Call("mcp_built_in__trigger_wait", IdleCall(depth: true)[IdleCall(true).IndexOf(' ')..],
                    "{\"structuredContent\":{\"outcome\":\"timeout\",\"observed\":[{\"conditionIndex\":0,"
                    + "\"type\":\"directory_idle\",\"entries\":2,\"depth\":2}]},\"isError\":false}")
            };
            (await RunAsync("trigger-wait-idle-timeout-diagnostics", noIdle, "410 ms", scratch.FullName))
                .Where(pair => pair.Key.Contains("idleMs", StringComparison.OrdinalIgnoreCase))
                .ShouldHaveSingleItem().Value.Passed.ShouldBeFalse();

            // The model must not read a timed-out wait as a settled folder.
            (await RunAsync("trigger-wait-idle-timeout-diagnostics", good,
                "The folder became quiet at depth 2.", scratch.FullName))
                .Where(pair => pair.Key.Contains("does not claim", StringComparison.OrdinalIgnoreCase))
                .ShouldHaveSingleItem().Value.Passed.ShouldBeFalse();
        }
        finally { scratch.Delete(recursive: true); }
    }
}
