namespace AI.Infrastructure.Tests.Tools;

using System.Diagnostics;
using AI.Contracts.FileSystem;
using AI.Mcp.BuiltIn.Grants;
using AI.Mcp.BuiltIn.Triggers;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class TriggerWaiterTests
{
    [Fact]
    public async Task ShouldDistinguishDelayFromTimeoutAndCancellation()
    {
        var waiter = NewWaiter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var fired = await waiter.WaitAsync([new TriggerCondition("delay", AfterMs: 20)], 1000, null, timeout.Token);
        fired.Outcome.ShouldBe("triggered");
        fired.ConditionIndex.ShouldBe(0);
        fired.TimeoutMs.ShouldBe(1000);

        var expired = await waiter.WaitAsync([new TriggerCondition("delay", AfterMs: 500)], 20, null, timeout.Token);
        expired.Outcome.ShouldBe("timeout");
        expired.ConditionIndex.ShouldBeNull();
        expired.TimeoutMs.ShouldBe(20);

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Should.ThrowAsync<OperationCanceledException>(() => waiter.WaitAsync(
            [new TriggerCondition("delay", AfterMs: 500)], 1000, null, cancelled.Token));
    }

    [Fact]
    public async Task ANewMessageJoiningTheTurnShouldEndTheWaitWithAnOutcome()
    {
        var waiter = NewWaiter();
        using var joined = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // The cancellation is the message joining the turn, which the turn announced by setting the
        // flag. Throwing here would leave the caller to invent a reply that matches no schema.
        var result = await waiter.WaitAsync([
            new TriggerCondition("delay", AfterMs: 60000, InterruptOnJoin: true)
        ], 60000, null, joined.Token);

        result.Outcome.ShouldBe("interrupted");
        result.Error.ShouldNotBeNull().ShouldContain("new message");
        result.TimeoutMs.ShouldBe(60000);
        result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().Type.ShouldBe("delay");
        result.ElapsedMs.ShouldBeLessThan(60000);
    }

    [Fact]
    public async Task AStoppedRunShouldStillCancelTheWait()
    {
        var waiter = NewWaiter();
        using var stopped = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Nothing announced a join, so the cancellation is the run being stopped and belongs to the
        // caller: swallowing it would tell a stopped run that its tool had answered.
        await Should.ThrowAsync<OperationCanceledException>(() => waiter.WaitAsync(
            [new TriggerCondition("delay", AfterMs: 60000)], 60000, null, stopped.Token));
    }

    [Fact]
    public async Task TimeoutShouldReportWhatEachConditionLastSaw()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-observed").FullName;
        try
        {
            var present = Path.Combine(root, "present.txt");
            var missing = Path.Combine(root, "missing.txt");
            await File.WriteAllTextAsync(present, "content", TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var result = await waiter.WaitAsync(
            [
                new TriggerCondition("file_exists", Path: missing),
                new TriggerCondition("file_missing", Path: present)
            ], 300, null, cancel.Token);

            result.Outcome.ShouldBe("timeout");
            result.Observed.ShouldNotBeNull().Length.ShouldBe(2);
            var absent = result.Observed!.Single(item => item.ConditionIndex == 0);
            absent.Type.ShouldBe("file_exists");
            absent.Exists.ShouldBe(false);
            absent.LengthBytes.ShouldBeNull();
            var existing = result.Observed.Single(item => item.ConditionIndex == 1);
            existing.Exists.ShouldBe(true);
            existing.LengthBytes.ShouldBe(7);
            existing.LastWriteTime.ShouldNotBeNull();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldTriggerWhenTheFileDisappears()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-missing").FullName;
        try
        {
            var path = Path.Combine(root, "lock.txt");
            await File.WriteAllTextAsync(path, "in use", TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var pending = waiter.WaitAsync([new TriggerCondition("file_missing", Path: path)],
                5000, null, cancel.Token);
            // A file that is there when the wait starts has to hold it: firing at once would report a
            // deletion that had not happened and turn the condition into "file does not exist".
            await Task.Delay(300, TestContext.Current.CancellationToken);
            pending.IsCompleted.ShouldBeFalse();

            File.Delete(path);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("file_missing");
            result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().Exists.ShouldBe(false);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AMatchedConditionShouldCarryItsObservations()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-match").FullName;
        try
        {
            var path = Path.Combine(root, "result.txt");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var pending = waiter.WaitAsync([new TriggerCondition("file_exists", Path: path)], 3000, null, cancel.Token);
            await File.WriteAllTextAsync(path, "done", TestContext.Current.CancellationToken);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            var seen = result.Observed.ShouldNotBeNull().ShouldHaveSingleItem();
            seen.Exists.ShouldBe(true);
            seen.LengthBytes.ShouldBe(4);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldReportProgressWhileWaiting()
    {
        var waiter = NewWaiter();
        var seen = new List<TriggerProgress>();
        var reports = new Progress<TriggerProgress>(report => { lock (seen) seen.Add(report); });
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var result = await waiter.WaitAsync([new TriggerCondition("delay", AfterMs: 2500)], 10000, reports, cancel.Token);

        result.Outcome.ShouldBe("triggered");
        SpinWait.SpinUntil(() =>
        {
            lock (seen) return seen.Count >= 2;
        }, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        // One report as the wait starts and one while it runs: a card that never updates cannot be
        // told apart from a call that hung.
        TriggerProgress[] captured;
        lock (seen) captured = seen.ToArray();
        captured.ShouldAllBe(report => report.TimeoutMs == 10000 && report.Conditions == 1);
        captured.ShouldContain(report => report.Message.Contains("delay"));
    }

    [Fact]
    public async Task ShouldWaitForAFileToChangeAndSettle()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger").FullName;
        try
        {
            var path = Path.Combine(root, "result.txt");
            await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var pending = waiter.WaitAsync([new TriggerCondition("file_changed", Path: path, StableForMs: 100)],
                3000, null, timeout.Token);
            await File.WriteAllTextAsync(path, "after", TestContext.Current.CancellationToken);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("file_changed");
            result.Path.ShouldBe(path);
            result.ElapsedMs.ShouldBeGreaterThanOrEqualTo(100);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CreatingAnAncestorDirectoryShouldNotCountAsAFileChange()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-parent").FullName;
        try
        {
            var parent = Path.Combine(root, "nested");
            var path = Path.Combine(parent, "result.txt");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var pending = waiter.WaitAsync([new TriggerCondition("file_changed", Path: path)],
                300, null, cancel.Token);
            Directory.CreateDirectory(parent);
            var result = await pending;

            result.Outcome.ShouldBe("timeout");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConditionsInOneDirectoryShouldShareAWatcher()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-shared").FullName;
        try
        {
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            await File.WriteAllTextAsync(first, "one", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(second, "two", TestContext.Current.CancellationToken);
            var watchers = new CountingFileWatcherFactory();
            var waiter = new TriggerWaiter(new PathGuard(new Grants(
                new DirectoryGrantSpec(root, true, new HashSet<GrantCapability> { GrantCapability.Read }))),
                new SystemFileSystem(), new SystemPath(), null, watchers);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var pending = waiter.WaitAsync(
            [
                new TriggerCondition("file_changed", Path: first),
                new TriggerCondition("file_changed", Path: second)
            ], 5000, null, cancel.Token);
            await File.WriteAllTextAsync(second, "changed", TestContext.Current.CancellationToken);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            result.ConditionIndex.ShouldBe(1);
            watchers.Created.ShouldBe(1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldCheckTheNamedProcessWithoutEnumeratingOthers()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var waiter = NewWaiter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await waiter.WaitAsync([
            new TriggerCondition("process_memory_above", ProcessId: process.Id, MemoryBytes: 1)
        ], 1000, null, timeout.Token);

        result.Outcome.ShouldBe("triggered");
        result.ProcessId.ShouldBe(process.Id);
        result.MemoryBytes!.Value.ShouldBeGreaterThan(1);
        result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().ProcessRunning.ShouldBe(true);
    }

    [Fact]
    public async Task ShouldTriggerWhenAProcessFallsBelowAMemoryThreshold()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var waiter = NewWaiter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // A terabyte is above any working set this host has, so the outcome is decided by the "below"
        // branch of the comparison and not by how much memory the test runner happens to use.
        const long ceiling = 1_000_000_000_000;
        var result = await waiter.WaitAsync([
            new TriggerCondition("process_memory_below", ProcessId: process.Id, MemoryBytes: ceiling)
        ], 2000, null, timeout.Token);

        result.Outcome.ShouldBe("triggered");
        result.Type.ShouldBe("process_memory_below");
        result.MemoryBytes!.Value.ShouldBeLessThan(ceiling);
        result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().ProcessRunning.ShouldBe(true);
    }

    [Fact]
    public async Task ShouldTriggerWhenASleepingProcessFallsBelowACpuThreshold()
    {
        using var process = StartSleeper();
        try
        {
            var waiter = NewWaiter();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // The first reading only establishes a baseline, so the condition cannot fire on a
            // percentage nothing has been measured against yet; a sleeper then reads as nearly zero.
            var result = await waiter.WaitAsync([
                new TriggerCondition("process_cpu_below", ProcessId: process.Id, CpuPercent: 100)
            ], 5000, null, cancel.Token);

            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("process_cpu_below");
            var observed = result.Observed.ShouldNotBeNull().ShouldHaveSingleItem();
            observed.CpuPercent.ShouldNotBeNull();
            observed.CpuPercent.Value.ShouldBeLessThan(100);
            observed.ProcessRunning.ShouldBe(true);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task ShouldTriggerWhenAProcessRisesAboveACpuThreshold()
    {
        var waiter = NewWaiter();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var busy = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        // Load has to come from somewhere: a sleeping process sits at zero and would show only that
        // the wait times out, never that the "above" branch is read.
        var work = Task.Run(() =>
        {
            while (!busy.IsCancellationRequested) { }
        }, CancellationToken.None);
        try
        {
            var result = await waiter.WaitAsync([
                new TriggerCondition("process_cpu_above", ProcessId: Environment.ProcessId, CpuPercent: 1)
            ], 10000, null, cancel.Token);

            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("process_cpu_above");
            var observed = result.Observed.ShouldNotBeNull().ShouldHaveSingleItem();
            observed.CpuPercent!.Value.ShouldBeGreaterThan(1);
        }
        finally
        {
            await busy.CancelAsync();
            await work;
        }
    }

    [Fact]
    public async Task ShouldWaitForAFileToContainAPattern()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-contains").FullName;
        try
        {
            var path = Path.Combine(root, "build.log");
            await File.WriteAllTextAsync(path, "starting\n", TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var pending = waiter.WaitAsync([
                new TriggerCondition("file_contains", Path: path, Pattern: "BUILD SUCCEEDED")
            ], 10000, null, cancel.Token);
            // Still waiting while the file exists without the line: existing is not the condition.
            await Task.Delay(300, TestContext.Current.CancellationToken);
            pending.IsCompleted.ShouldBeFalse();

            await File.WriteAllTextAsync(path, "starting\nBUILD SUCCEEDED\n", TestContext.Current.CancellationToken);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("file_contains");
            result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().Matched.ShouldBe(true);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldWaitForAPatternInAFileThatDoesNotExistYet()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-contains-new").FullName;
        try
        {
            var path = Path.Combine(root, "result.txt");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var pending = waiter.WaitAsync([new TriggerCondition("file_contains", Path: path, Pattern: "ok")],
                10000, null, cancel.Token);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path, "ok\n", TestContext.Current.CancellationToken);

            (await pending).Outcome.ShouldBe("triggered");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldTreatAPatternAsALiteralUnlessRegexIsAskedFor()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-literal").FullName;
        try
        {
            var path = Path.Combine(root, "log.txt");
            await File.WriteAllTextAsync(path, "needle in a haystack\n", TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            // "need.e" is a literal string here, and the file says "needle": no dot, no match.
            var literal = await waiter.WaitAsync([
                new TriggerCondition("file_contains", Path: path, Pattern: "need.e")
            ], 300, null, cancel.Token);
            literal.Outcome.ShouldBe("timeout");
            literal.Observed.ShouldNotBeNull().ShouldHaveSingleItem().Matched.ShouldBe(false);

            // The same text as a regular expression does match, and ignoring case widens it again.
            (await waiter.WaitAsync([
                new TriggerCondition("file_contains", Path: path, Pattern: "need.e", Regex: true)
            ], 1000, null, cancel.Token)).Outcome.ShouldBe("triggered");
            (await waiter.WaitAsync([
                new TriggerCondition("file_contains", Path: path, Pattern: "NEEDLE", IgnoreCase: true)
            ], 1000, null, cancel.Token)).Outcome.ShouldBe("triggered");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldWaitForAFileToStayIdle()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-idle").FullName;
        try
        {
            var path = Path.Combine(root, "stream.log");
            await File.WriteAllTextAsync(path, "line 1\n", TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var pending = waiter.WaitAsync([
                new TriggerCondition("file_idle", Path: path, QuietMs: 800)
            ], 10000, null, cancel.Token);
            // A writer keeps touching the file well past the quiet period: idleness has to be
            // earned by the silence after the last change, not by the wait having started.
            var writing = Stopwatch.StartNew();
            while (writing.ElapsedMilliseconds < 1200)
            {
                await File.AppendAllTextAsync(path, "more\n", TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
                pending.IsCompleted.ShouldBeFalse();
            }

            var result = await pending;
            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("file_idle");
            result.ElapsedMs.ShouldBeGreaterThanOrEqualTo(1100);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldWaitForADirectoryToStayIdle()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-dir-idle").FullName;
        try
        {
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "one.txt"), "first");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var pending = waiter.WaitAsync([
                new TriggerCondition("directory_idle", Path: output, QuietMs: 800)
            ], 10000, null, cancel.Token);
            // A build keeps adding entries to the directory past the quiet period: idleness has to
            // come from the silence after the last write, not from the wait having started.
            var writing = Stopwatch.StartNew();
            var created = 1;
            while (writing.ElapsedMilliseconds < 1200)
            {
                await File.WriteAllTextAsync(Path.Combine(output, $"part-{created++}.txt"), "more",
                    TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
                pending.IsCompleted.ShouldBeFalse();
            }

            var result = await pending;
            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("directory_idle");
            result.Path.ShouldBe(output);
            result.ElapsedMs.ShouldBeGreaterThanOrEqualTo(1100);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldTriggerDirectoryIdleWhenNothingMoved()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-dir-quiet").FullName;
        try
        {
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "one.txt"), "first");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var result = await waiter.WaitAsync([
                new TriggerCondition("directory_idle", Path: output, QuietMs: 200)
            ], 10000, null, cancel.Token);
            result.Outcome.ShouldBe("triggered");
            result.ElapsedMs.ShouldBeGreaterThanOrEqualTo(150);
            // An idle directory holds no text to search, so the observation reports no window.
            result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().SearchedBytes.ShouldBeNull();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldCountTheLevelsTheDirectoryWasAskedFor()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-dir-depth").FullName;
        try
        {
            var output = Path.Combine(root, "output");
            var nested = Path.Combine(output, "nested");
            Directory.CreateDirectory(nested);
            var part = Path.Combine(nested, "part.txt");
            File.WriteAllText(part, "first");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // One level would see the nested directory alone and call it quiet; two levels count what
            // is written inside it, which is what a caller watching build output one directory down asks.
            var pending = waiter.WaitAsync([
                new TriggerCondition("directory_idle", Path: output, QuietMs: 800, Depth: 2)
            ], 10000, null, cancel.Token);
            var writing = Stopwatch.StartNew();
            while (writing.ElapsedMilliseconds < 1200)
            {
                // Rewritten in place: creating entries would move the nested directory's own timestamp
                // and make the default depth look as attentive as this one.
                await File.WriteAllTextAsync(part, $"more {writing.ElapsedMilliseconds}",
                    TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
                pending.IsCompleted.ShouldBeFalse();
            }

            var result = await pending;
            result.Outcome.ShouldBe("triggered");
            result.Type.ShouldBe("directory_idle");
            result.ElapsedMs.ShouldBeGreaterThanOrEqualTo(1100);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TheDefaultDepthShouldNotLookPastTheDirectoryItself()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-dir-shallow").FullName;
        try
        {
            var output = Path.Combine(root, "output");
            var nested = Path.Combine(output, "nested");
            Directory.CreateDirectory(nested);
            var part = Path.Combine(nested, "part.txt");
            File.WriteAllText(part, "first");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var pending = waiter.WaitAsync([
                new TriggerCondition("directory_idle", Path: output, QuietMs: 300)
            ], 10000, null, cancel.Token);
            // Depth is 1 unless it is asked for: a write inside the nested directory is not this
            // condition's business, and waiting for it would make the default wait forever.
            var writing = Stopwatch.StartNew();
            while (writing.ElapsedMilliseconds < 500)
            {
                await File.WriteAllTextAsync(part, $"more {writing.ElapsedMilliseconds}",
                    TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            var result = await pending;
            result.Outcome.ShouldBe("triggered");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AnEmptyDirectoryHasNoNewestWriteToReport()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-dir-empty").FullName;
        try
        {
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Nothing inside means nothing was written: a timestamp invented for an entry that does
            // not exist reads as a real, ancient write and hides that the count is zero.
            var result = await waiter.WaitAsync([
                new TriggerCondition("directory_idle", Path: output, QuietMs: 60000)
            ], 700, null, cancel.Token);

            result.Outcome.ShouldBe("timeout");
            var observed = result.Observed.ShouldNotBeNull().ShouldHaveSingleItem();
            observed.Entries.ShouldBe(0);
            observed.Depth.ShouldBe(1);
            observed.LastWriteTime.ShouldBeNull();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ATimedOutIdleConditionShouldSayHowFarTheSilenceHadCome()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-idle-report").FullName;
        try
        {
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "one.txt"), "first");
            var file = Path.Combine(root, "stream.log");
            File.WriteAllText(file, "line");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // A writer that never stops: the quiet period is never reached, and the timeout has to
            // say how close it came instead of reporting a bare absence of news.
            var pending = waiter.WaitAsync([
                new TriggerCondition("directory_idle", Path: output, QuietMs: 60000, Depth: 2),
                new TriggerCondition("file_idle", Path: file, QuietMs: 60000)
            ], 700, null, cancel.Token);
            var writing = Stopwatch.StartNew();
            var written = 1;
            while (writing.ElapsedMilliseconds < 900)
            {
                await File.WriteAllTextAsync(Path.Combine(output, $"part-{written++}.txt"), "more",
                    TestContext.Current.CancellationToken);
                await File.AppendAllTextAsync(file, "\nmore", TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            var result = await pending;
            result.Outcome.ShouldBe("timeout");
            var observed = result.Observed.ShouldNotBeNull();

            // What the directory walk actually counted, so "quiet" can be read together with it.
            var directory = observed.Single(item => item.ConditionIndex == 0);
            directory.Exists.ShouldBe(true);
            directory.Entries.ShouldNotBeNull().ShouldBeGreaterThan(1);
            directory.Depth.ShouldBe(2);
            directory.LastWriteTime.ShouldNotBeNull();
            // The last write restarted the period a moment ago, so little of the minute had passed.
            directory.IdleMs.ShouldNotBeNull().ShouldBeLessThan(60000);

            var stream = observed.Single(item => item.ConditionIndex == 1);
            stream.IdleMs.ShouldNotBeNull().ShouldBeLessThan(60000);
            // A file has no entries and no levels to count, and says so by leaving them out.
            stream.Entries.ShouldBeNull();
            stream.Depth.ShouldBeNull();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldRejectADirectoryConditionThatNamesSomethingElse()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-dir-invalid").FullName;
        try
        {
            var file = Path.Combine(root, "log.txt");
            File.WriteAllText(file, "text");
            var missing = Path.Combine(root, "absent");
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // A file is not a directory, so waiting for its entries to go quiet is a mistake to report.
            var notADirectory = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("directory_idle", Path: file, QuietMs: 100)
                ], 1000, null, cancel.Token));
            notADirectory.Message.ShouldContain("existing directory");

            var gone = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("directory_idle", Path: missing, QuietMs: 100)
                ], 1000, null, cancel.Token));
            gone.Message.ShouldContain("existing directory");

            // quietMs is the whole point of the condition, and a field it does not read is refused.
            var noQuiet = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("directory_idle", Path: root)
                ], 1000, null, cancel.Token));
            noQuiet.Message.ShouldContain("quietMs");

            var pattern = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("directory_idle", Path: root, QuietMs: 100, Pattern: "x")
                ], 1000, null, cancel.Token));
            pattern.Message.ShouldContain("pattern");

            // A depth of zero would count nothing at all, and the level limit is the one the walk has.
            var noDepth = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("directory_idle", Path: root, QuietMs: 100, Depth: 0)
                ], 1000, null, cancel.Token));
            noDepth.Message.ShouldContain("depth in 1.." + TriggerWaiter.MaxDepth);

            var tooDeep = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("directory_idle", Path: root, QuietMs: 100,
                        Depth: TriggerWaiter.MaxDepth + 1)
                ], 1000, null, cancel.Token));
            tooDeep.Message.ShouldContain("depth");

            // depth belongs to directory_idle alone: a file has no levels to count.
            var fileDepth = await Should.ThrowAsync<ArgumentException>(async () =>
                await waiter.WaitAsync([
                    new TriggerCondition("file_idle", Path: file, QuietMs: 100, Depth: 2)
                ], 1000, null, cancel.Token));
            fileDepth.Message.ShouldContain("depth");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ADirectoryListingShouldCarryTheWriteTimeOfEveryEntry()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-listing-time").FullName;
        try
        {
            var files = new SystemFileSystem();
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(first, "one");
            File.WriteAllText(second, "two");
            var older = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var newer = new DateTimeOffset(2021, 6, 7, 8, 9, 10, TimeSpan.Zero);
            File.SetLastWriteTimeUtc(first, older.UtcDateTime);
            File.SetLastWriteTimeUtc(second, newer.UtcDateTime);

            var entries = await files.ListEntriesAsync(root, recursive: false, TestContext.Current.CancellationToken);

            // The listing is what directory_idle polls with: a timestamp it had to fetch per entry
            // afterwards is exactly what made a large directory cost more than the poll interval.
            entries.Single(entry => entry.Name == "first.txt").LastWriteTime.ShouldBe(older);
            entries.Single(entry => entry.Name == "second.txt").LastWriteTime.ShouldBe(newer);
            var single = await files.GetEntryAsync(first, TestContext.Current.CancellationToken);
            single.ShouldNotBeNull().LastWriteTime.ShouldBe(older);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldRejectATextConditionWithoutPatternOrWithABadExpression()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-text-invalid").FullName;
        try
        {
            var path = Path.Combine(root, "log.txt");
            File.WriteAllText(path, "text");
            var waiter = NewWaiter(root);

            (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                    [new TriggerCondition("file_contains", Path: path)], 1000, null, CancellationToken.None)))
                .Message.ShouldContain("requires pattern");
            (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                    [new TriggerCondition("file_contains", Path: path, Pattern: "(", Regex: true)],
                    1000, null, CancellationToken.None)))
                .Message.ShouldContain("invalid regular expression");
            (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                    [new TriggerCondition("file_contains", Path: path, Pattern: "text", QuietMs: 100)],
                    1000, null, CancellationToken.None)))
                .Message.ShouldContain("does not read quietMs");
            (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                    [new TriggerCondition("file_idle", Path: path)], 1000, null, CancellationToken.None)))
                .Message.ShouldContain("requires quietMs");
            (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                    [new TriggerCondition("process_output_contains", ProcessId: Environment.ProcessId, Pattern: "x")],
                    1000, null, CancellationToken.None)))
                .Message.ShouldContain("does not read processId");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ATextConditionShouldSayHowMuchTextItSearched()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-window").FullName;
        try
        {
            // Small file: the whole text is read, so a miss means the line was never written.
            var small = Path.Combine(root, "small.log");
            await File.WriteAllTextAsync(small, "alpha\nbeta\n", TestContext.Current.CancellationToken);
            // Large file: only the tail fits the window, so a miss may simply be older than it.
            var large = Path.Combine(root, "large.log");
            await File.WriteAllTextAsync(large,
                "EARLY LINE\n" + string.Concat(Enumerable.Repeat("padding line\n", 40000)),
                TestContext.Current.CancellationToken);
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var read = await waiter.WaitAsync([
                new TriggerCondition("file_contains", Path: small, Pattern: "missing")
            ], 300, null, cancel.Token);
            var whole = read.Observed.ShouldNotBeNull().ShouldHaveSingleItem();
            whole.Matched.ShouldBe(false);
            whole.SearchedBytes.ShouldBe(new FileInfo(small).Length);
            whole.OlderTextUnsearched.ShouldBe(false);

            var truncated = await waiter.WaitAsync([
                new TriggerCondition("file_contains", Path: large, Pattern: "EARLY LINE")
            ], 300, null, cancel.Token);
            var tail = truncated.Observed.ShouldNotBeNull().ShouldHaveSingleItem();
            tail.Matched.ShouldBe(false);
            tail.OlderTextUnsearched.ShouldBe(true);
            tail.SearchedBytes.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TriggerWaiter.TailBytes);
            tail.SearchedBytes!.Value.ShouldBeGreaterThan(0);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <remarks>
    /// The test ends the process itself rather than waiting for it to exit on its own: a child that
    /// sleeps for a second also has to start, and PowerShell alone takes most of two seconds to do
    /// that on an idle machine and far longer under a full test run, which used to miss the window.
    /// What is under test is that the waiter notices an exit, not how long a shell takes to start.
    /// </remarks>
    [Fact]
    public async Task ShouldObserveProcessExit()
    {
        using var process = StartSleeper();
        try
        {
            var waiter = NewWaiter();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var pending = waiter.WaitAsync([
                new TriggerCondition("process_exit", ProcessId: process.Id)
            ], 20000, null, timeout.Token);
            // Still running after the waiter has looked once: an exit before it started would not
            // show that it watches for one.
            await Task.Delay(300, TestContext.Current.CancellationToken);
            pending.IsCompleted.ShouldBeFalse();

            process.Kill(entireProcessTree: true);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            result.ProcessId.ShouldBe(process.Id);
            result.Observed.ShouldNotBeNull().ShouldHaveSingleItem().ProcessRunning.ShouldBe(false);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task ShouldNoticeAnExitWithinThePollInterval()
    {
        // An exit condition used to be rechecked only every 250 ms, so a process that ended right
        // after the wait started cost the whole interval. A terminal notification does not.
        using var process = StartSleeper();
        try
        {
            var waiter = NewWaiter();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // What is being measured is the waiter's own lag, so the clock starts at the signal
            // that ends the process. The sleeper starts no children, so it is stopped without the
            // process-tree walk: that walk scans /proc for every descendant, and on a loaded
            // runner it alone outlasted the poll interval and failed this by a millisecond while
            // the notification was never late.
            var pending = waiter.WaitAsync([new TriggerCondition("process_exit", ProcessId: process.Id)],
                20000, null, cancel.Token);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var signalledAt = System.Diagnostics.Stopwatch.GetTimestamp();
            process.Kill();
            var result = await pending;
            var lagMs = (System.Diagnostics.Stopwatch.GetTimestamp() - signalledAt) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;

            result.Outcome.ShouldBe("triggered");
            lagMs.ShouldBeLessThan(200);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task ShouldDisposeFileObserverWhenRunIsCancelled()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger-cancel").FullName;
        try
        {
            var waiter = NewWaiter(root);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Should.ThrowAsync<OperationCanceledException>(() => waiter.WaitAsync([
                new TriggerCondition("file_exists", Path: Path.Combine(root, "never.txt"))
            ], 3000, null, cancel.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldRejectFileOutsideReadGrant()
    {
        var waiter = NewWaiter();
        await Should.ThrowAsync<GrantException>(() => waiter.WaitAsync([
            new TriggerCondition("file_exists", Path: Path.Combine(Path.GetTempPath(), "ungranted.txt"))
        ], 1000, null, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldNameTheConditionInValidationErrors()
    {
        var waiter = NewWaiter();
        var error = await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync([
            new TriggerCondition("delay", AfterMs: 10),
            new TriggerCondition("process_memory_above", ProcessId: Environment.ProcessId)
        ], 1000, null, CancellationToken.None));
        error.Message.ShouldContain("Condition 1 ('process_memory_above')");
        error.Message.ShouldContain("memoryBytes");
    }

    [Fact]
    public async Task ShouldRejectAFieldTheTypeDoesNotRead()
    {
        var waiter = NewWaiter();
        // The slip that matters: the name a model reaches for first is not the one the tool reads.
        var error = await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync([
            new TriggerCondition("file_exists", Path: Path.GetTempPath(), CpuPercent: 5)
        ], 1000, null, CancellationToken.None));
        error.Message.ShouldContain("does not read cpuPercent");
        error.Message.ShouldContain("path, stableForMs");
    }

    [Fact]
    public async Task ShouldRejectStableForMsOnAnExitCondition()
    {
        var waiter = NewWaiter();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var error = await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync([
            new TriggerCondition("process_exit", ProcessId: process.Id, StableForMs: 100)
        ], 1000, null, CancellationToken.None));
        error.Message.ShouldContain("does not read stableForMs");
    }

    [Fact]
    public async Task ShouldRejectUnknownTypeAndOutOfRangeDelay()
    {
        var waiter = NewWaiter();
        (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                [new TriggerCondition("file_appears", Path: Path.GetTempPath())], 1000, null, CancellationToken.None)))
            .Message.ShouldContain("unknown type");
        (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                [new TriggerCondition("delay", AfterMs: 0)], 1000, null, CancellationToken.None)))
            .Message.ShouldContain("afterMs");
        (await Should.ThrowAsync<ArgumentException>(() => waiter.WaitAsync(
                [new TriggerCondition("process_cpu_above", ProcessId: Environment.ProcessId)], 1000, null,
                    CancellationToken.None)))
            .Message.ShouldContain("cpuPercent");
    }

    private static TriggerWaiter NewWaiter(string? root = null) => new(root is null
        ? new PathGuard(new Grants())
        : new PathGuard(new Grants(new DirectoryGrantSpec(root, true,
            new HashSet<GrantCapability> { GrantCapability.Read }))));

    private static System.Diagnostics.Process StartSleeper()
    {
        var start = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" })
                start.ArgumentList.Add(argument);
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("sleep 60");
        }

        return System.Diagnostics.Process.Start(start)!;
    }

    /// <summary>Counts the platform watchers a wait creates, so sharing one is observable.</summary>
    private sealed class CountingFileWatcherFactory : IFileWatcherFactory
    {
        private int _created;

        public int Created => Volatile.Read(ref _created);

        public IFileWatcher Create(string directory, bool recursive)
        {
            Interlocked.Increment(ref _created);
            return new CountingFileWatcher();
        }
    }

    private sealed class CountingFileWatcher : IFileWatcher
    {
        public event Action<string>? Changed { add { } remove { } }
        public event Action<string, string>? Renamed { add { } remove { } }
        public event Action? Faulted { add { } remove { } }

        public void Start() { }

        public void Dispose() { }
    }

    private sealed class Grants(params DirectoryGrantSpec[] grants) : IGrantSource
    {
        public IReadOnlyList<DirectoryGrantSpec> Load() => grants;
    }
}
