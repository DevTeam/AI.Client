namespace AI.Infrastructure.Tests.Tools;

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
            var pending = waiter.WaitAsync([new TriggerCondition("process_exit", ProcessId: process.Id)],
                20000, null, cancel.Token);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var started = System.Diagnostics.Stopwatch.StartNew();
            process.Kill(entireProcessTree: true);
            var result = await pending;

            result.Outcome.ShouldBe("triggered");
            started.ElapsedMilliseconds.ShouldBeLessThan(200);
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
