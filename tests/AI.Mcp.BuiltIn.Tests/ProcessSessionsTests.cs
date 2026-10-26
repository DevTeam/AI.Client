using System.Text;
using AI.Mcp.BuiltIn.Process;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;
using Shouldly;
using Xunit;

namespace AI.Mcp.BuiltIn.Tests;

public sealed class ProcessSessionsTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("ai-process-session-test-").FullName;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepeatedWaitsContinueOneExecutionAndPreserveLaunchBounds(bool sandboxed)
    {
        var runner = new ControlledRunner("first", "warning");
        await using var sessions = new ProcessSessions(runner, new TestSandbox(_root));
        var request = new ProcessRequest("program", ["test"], _root, 12345,
            ChildProcessLimits.Create(512, 8), sandboxed);
        var started = await sessions.RunAsync(request, 0, Token);
        started.Status.ShouldBe("running");
        started.ExitCode.ShouldBeNull();
        await runner.Written.Task.WaitAsync(Token);
        var first = await sessions.WaitAsync(started.ProcessId!, 1, 0, 0, Token);
        first.Status.ShouldBe("running");
        first.Stdout.ShouldBe("first");
        first.Stderr.ShouldBe("warning");
        var second = await sessions.WaitAsync(started.ProcessId!, 1, first.StdoutOffset, first.StderrOffset, Token);
        second.Status.ShouldBe("running");
        second.Stdout.ShouldBeEmpty();
        second.Stderr.ShouldBeEmpty();
        runner.Release.TrySetResult();
        var final = await sessions.WaitAsync(started.ProcessId!, 1000, second.StdoutOffset, second.StderrOffset, Token);
        final.Status.ShouldBe("completed");
        final.ExitCode.ShouldBe(7);
        final.Stdout.ShouldBe("last");
        runner.Calls.ShouldBe(1);
        runner.Request.ShouldBe(request);
        (await File.ReadAllTextAsync(final.StdoutPath!, Token)).ShouldBe("firstlast");
        (await File.ReadAllTextAsync(final.StderrPath!, Token)).ShouldBe("warning");
    }

    [Fact]
    public async Task LongUnicodeOutputCanBePagedReplayedAndReadInFullFromLogs()
    {
        var content = new string('x', ProcessRunner.OutputLimit - 1) + "🌍привет" + new string('y', ProcessRunner.OutputLimit * 2);
        var runner = new ControlledRunner(content, "diagnostic");
        await using var sessions = new ProcessSessions(runner, new TestSandbox(_root));
        var started = await sessions.RunAsync(new ProcessRequest("program", [], _root), 0, Token);
        await runner.Written.Task.WaitAsync(Token);
        runner.Release.TrySetResult();
        var page = await sessions.WaitAsync(started.ProcessId!, 1000, 0, 0, Token);
        page.Status.ShouldBe("completed");
        page.HasMoreOutput.ShouldBeTrue();
        page.Stdout.ShouldBe(new string('x', ProcessRunner.OutputLimit - 1));
        var all = new StringBuilder(page.Stdout);
        while (page.HasMoreOutput)
        {
            page = await sessions.WaitAsync(started.ProcessId!, 0, page.StdoutOffset, page.StderrOffset, Token);
            all.Append(page.Stdout);
        }
        all.ToString().ShouldBe(content + "last");
        page.Truncated.ShouldBeFalse();
        (await File.ReadAllTextAsync(page.StdoutPath!, Token)).ShouldBe(content + "last");
        var replay = await sessions.WaitAsync(started.ProcessId!, 0, 0, 0, Token);
        replay.Stdout.ShouldBe(new string('x', ProcessRunner.OutputLimit - 1));
    }

    [Fact]
    public async Task CancellingAWaitLeavesTheProcessAliveButStopEndsItAndPreservesPartialLogs()
    {
        var runner = new ControlledRunner("partial", "");
        await using var sessions = new ProcessSessions(runner, new TestSandbox(_root));
        var started = await sessions.RunAsync(new ProcessRequest("program", [], _root), 0, Token);
        await runner.Written.Task.WaitAsync(Token);
        using var cancel = new CancellationTokenSource();
        var wait = sessions.WaitAsync(started.ProcessId!, 60000, 0, 0, cancel.Token);
        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => wait);
        runner.Stopped.ShouldBeFalse();
        var stopped = await sessions.StopAsync(started.ProcessId!, Token);
        stopped.Status.ShouldBe("stopped");
        stopped.ExitCode.ShouldBeNull();
        stopped.Stdout.ShouldBe("partial");
        runner.Stopped.ShouldBeTrue();
        (await sessions.StopAsync(started.ProcessId!, Token)).ShouldBe(stopped);
    }

    [Fact]
    public async Task CancellingALaunchBeforeItReturnsAHandleStopsItsProcess()
    {
        var runner = new ControlledRunner("partial", "");
        await using var sessions = new ProcessSessions(runner, new TestSandbox(_root));
        using var cancel = new CancellationTokenSource();
        var launch = sessions.RunAsync(new ProcessRequest("program", [], _root), 60000, cancel.Token);
        await runner.Written.Task.WaitAsync(Token);
        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => launch);
        runner.Stopped.ShouldBeTrue();
    }

    [Fact]
    public async Task ShutdownStopsOwnedWorkAndAnotherSessionCannotAddressIt()
    {
        var runner = new ControlledRunner("partial", "");
        var sessions = new ProcessSessions(runner, new TestSandbox(_root));
        await using var other = new ProcessSessions(new ControlledRunner("", ""), new TestSandbox(_root));
        try
        {
            var started = await sessions.RunAsync(new ProcessRequest("program", [], _root), 0, Token);
            await runner.Written.Task.WaitAsync(Token);
            (await other.StopAsync(started.ProcessId!, Token)).Status.ShouldBe("failed");
            (await other.WaitAsync(started.ProcessId!, 0, 0, 0, Token)).Status.ShouldBe("failed");
            runner.Stopped.ShouldBeFalse();
        }
        finally { await sessions.DisposeAsync(); }
        runner.Stopped.ShouldBeTrue();
    }

    [Fact]
    public async Task TheHardDeadlineStillStopsARealProcessAfterReturningItsHandle()
    {
        await using var sessions = new ProcessSessions(new ProcessRunner(), new TestSandbox(_root));
        var request = OperatingSystem.IsWindows()
            ? new ProcessRequest("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 120"], _root, 1000, Sandboxed: false)
            : new ProcessRequest("/bin/sh", ["-c", "sleep 120"], _root, 1000, Sandboxed: false);
        var started = await sessions.RunAsync(request, 0, Token);
        started.Status.ShouldBe("running");
        var final = await sessions.WaitAsync(started.ProcessId!, 10000, 0, 0, Token);
        final.Status.ShouldBe("timed_out");
        final.TimedOut.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndShutdownKillTheRealOwnedProcessTree(bool shutdown)
    {
        var sessions = new ProcessSessions(new ProcessRunner(), new TestSandbox(_root));
        System.Diagnostics.Process? child = null;
        try
        {
            var request = OperatingSystem.IsWindows()
                ? new ProcessRequest("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
                    "$child = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 600'; [Console]::WriteLine($child.Id); Start-Sleep -Seconds 600"], _root, 600000, Sandboxed: false)
                : new ProcessRequest("/bin/sh", ["-c", "sleep 600 & child=$!; echo $child; wait"], _root, 600000, Sandboxed: false);
            var started = await sessions.RunAsync(request, 0, Token);
            var page = started;
            // Two cold PowerShell starts on a loaded windows-latest runner once took over a minute
            // before the child's PID was printed, so the readiness budget has to stay well above the
            // time a shell may need to start; the process's own deadline above is longer than this
            // budget, and its sleep longer than both, so a slow start cannot be mistaken for an exit.
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(Token);
            ready.CancelAfter(TimeSpan.FromSeconds(300));
            while (!page.Stdout.Contains('\n', StringComparison.Ordinal))
            {
                page.Status.ShouldBe("running", page.ToString());
                page = await sessions.WaitAsync(started.ProcessId!, 50, 0, 0, ready.Token);
            }
            var pid = int.Parse(page.Stdout.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            child = System.Diagnostics.Process.GetProcessById(pid);
            if (shutdown) await sessions.DisposeAsync();
            else (await sessions.StopAsync(started.ProcessId!, Token)).Status.ShouldBe("stopped");
            await child.WaitForExitAsync(Token).WaitAsync(TimeSpan.FromSeconds(60), Token);
            child.HasExited.ShouldBeTrue();
        }
        finally
        {
            await sessions.DisposeAsync();
            // Cleanup only the child whose PID this test's own command returned.
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    private sealed class ControlledRunner(string stdout, string stderr) : IProcessRunner
    {
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public ProcessRequest? Request { get; private set; }
        public bool Stopped { get; private set; }
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken, ProcessOutput? output = null)
        {
            Calls++;
            Request = request;
            try
            {
                await output!.StandardOutput.WriteAsync(Encoding.UTF8.GetBytes(stdout), cancellationToken);
                await output.StandardError.WriteAsync(Encoding.UTF8.GetBytes(stderr), cancellationToken);
                await output.StandardOutput.FlushAsync(cancellationToken);
                await output.StandardError.FlushAsync(cancellationToken);
                Written.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                await output.StandardOutput.WriteAsync("last"u8.ToArray(), cancellationToken);
                return new ProcessResult(7, stdout + "last", stderr, 1, false, false, null);
            }
            catch (OperationCanceledException)
            {
                Stopped = true;
                throw;
            }
        }
    }

    private sealed class TestSandbox(string directory) : IToolSandbox
    {
        public SandboxPolicy? Policy(SandboxedTool tool) => null;
        public string TemporaryDirectory => directory;
        public string Describe(SandboxedTool tool) => "Test sandbox.";
    }
}
