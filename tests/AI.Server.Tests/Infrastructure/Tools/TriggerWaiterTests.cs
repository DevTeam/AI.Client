namespace AI.Infrastructure.Tests.Tools;

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
        var waiter = new TriggerWaiter(new PathGuard(new Grants()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var fired = await waiter.WaitAsync([new TriggerCondition("delay", AfterMs: 20)], 1000, timeout.Token);
        fired.Outcome.ShouldBe("triggered");
        fired.ConditionIndex.ShouldBe(0);

        var expired = await waiter.WaitAsync([new TriggerCondition("delay", AfterMs: 500)], 20, timeout.Token);
        expired.Outcome.ShouldBe("timeout");
        expired.ConditionIndex.ShouldBeNull();

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Should.ThrowAsync<OperationCanceledException>(() => waiter.WaitAsync(
            [new TriggerCondition("delay", AfterMs: 500)], 1000, cancelled.Token));
    }

    [Fact]
    public async Task ShouldWaitForAFileToChangeAndSettle()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-trigger").FullName;
        try
        {
            var path = Path.Combine(root, "result.txt");
            File.WriteAllText(path, "before");
            var waiter = new TriggerWaiter(new PathGuard(new Grants(
                new DirectoryGrantSpec(root, true, new HashSet<GrantCapability> { GrantCapability.Read }))));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var pending = waiter.WaitAsync([new TriggerCondition("file_changed", Path: path, StableForMs: 100)],
                3000, timeout.Token);
            File.WriteAllText(path, "after");
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
            var waiter = new TriggerWaiter(new PathGuard(new Grants(
                new DirectoryGrantSpec(root, true, new HashSet<GrantCapability> { GrantCapability.Read }))));
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var pending = waiter.WaitAsync([new TriggerCondition("file_changed", Path: path)],
                300, cancel.Token);
            Directory.CreateDirectory(parent);
            var result = await pending;

            result.Outcome.ShouldBe("timeout");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldCheckTheNamedProcessWithoutEnumeratingOthers()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var waiter = new TriggerWaiter(new PathGuard(new Grants()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await waiter.WaitAsync([
            new TriggerCondition("process_memory_above", ProcessId: process.Id, MemoryBytes: 1)
        ], 1000, timeout.Token);

        result.Outcome.ShouldBe("triggered");
        result.ProcessId.ShouldBe(process.Id);
        result.MemoryBytes!.Value.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task ShouldObserveProcessExit()
    {
        var start = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 1" })
                start.ArgumentList.Add(argument);
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("sleep 1");
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        try
        {
            var waiter = new TriggerWaiter(new PathGuard(new Grants()));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await waiter.WaitAsync([
                new TriggerCondition("process_exit", ProcessId: process.Id)
            ], 4000, timeout.Token);
            result.Outcome.ShouldBe("triggered");
            result.ProcessId.ShouldBe(process.Id);
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
            var waiter = new TriggerWaiter(new PathGuard(new Grants(
                new DirectoryGrantSpec(root, true, new HashSet<GrantCapability> { GrantCapability.Read }))));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Should.ThrowAsync<OperationCanceledException>(() => waiter.WaitAsync([
                new TriggerCondition("file_exists", Path: Path.Combine(root, "never.txt"))
            ], 3000, cancel.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldRejectFileOutsideReadGrant()
    {
        var waiter = new TriggerWaiter(new PathGuard(new Grants()));
        await Should.ThrowAsync<GrantException>(() => waiter.WaitAsync([
            new TriggerCondition("file_exists", Path: Path.Combine(Path.GetTempPath(), "ungranted.txt"))
        ], 1000, CancellationToken.None));
    }

    private sealed class Grants(params DirectoryGrantSpec[] grants) : IGrantSource
    {
        public IReadOnlyList<DirectoryGrantSpec> Load() => grants;
    }
}
