namespace AI.Sandbox.Tests;

using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using Shouldly;
using Xunit;

/// <summary>
/// Starts real processes through the launcher on whatever OS runs the tests: AppContainer on
/// Windows, Landlock on Linux and Seatbelt on macOS. Each test opens one directory for reading and
/// one for writing and checks what a shell command can and cannot do around them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProcessLauncherTests : IDisposable
{
    private readonly ProcessLauncher _launcher = new();
    private readonly string _root;
    private readonly string _readable;
    private readonly string _writable;
    private readonly string _closed;

    public ProcessLauncherTests()
    {
        _root = Directory.CreateTempSubdirectory("ai-sandbox-tests-").FullName;
        _readable = Directory.CreateDirectory(Path.Combine(_root, "readable")).FullName;
        _writable = Directory.CreateDirectory(Path.Combine(_root, "writable")).FullName;
        _closed = Directory.CreateDirectory(Path.Combine(_root, "closed")).FullName;
        File.WriteAllText(Path.Combine(_readable, "open.txt"), "open-content");
        File.WriteAllText(Path.Combine(_closed, "secret.txt"), "secret-content");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private SandboxPolicy Policy => new([new SandboxPath(_readable, SandboxAccess.Read), new SandboxPath(_writable, SandboxAccess.ReadWrite)]);

    /// <summary>
    /// Retries for a moment: a killed process lets go of its working directory only once Windows has
    /// finished tearing it down, which can be just after the kill returned.
    /// </summary>
    public void Dispose()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }

    [Fact]
    public async Task RunsAnUnconfinedProgramAndReportsItsExitCode()
    {
        var result = await RunAsync(Command(Shell.Exit(7)), policy: null);

        result.ExitCode.ShouldBe(7);
        result.TimedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task AConfinedProgramReadsADirectoryThePolicyOpens()
    {
        Shell.RequireSandbox(_launcher);

        var result = await RunAsync(Command(Shell.Read(Path.Combine(_readable, "open.txt"))), Policy);

        result.ExitCode.ShouldBe(0, result.Stderr);
        result.Stdout.ShouldContain("open-content");
    }

    [Fact]
    public async Task AConfinedProgramCannotReadOutsideThePolicy()
    {
        Shell.RequireSandbox(_launcher);

        var result = await RunAsync(Command(Shell.Read(Path.Combine(_closed, "secret.txt"))), Policy);

        result.ExitCode.ShouldNotBe(0);
        result.Stdout.ShouldNotContain("secret-content");
    }

    [Fact]
    public async Task WindowsAncestorsAllowMetadataButNotListingOrFileContents()
    {
        if (!OperatingSystem.IsWindows()) return;
        Shell.RequireSandbox(_launcher);
        var secret = Path.Combine(_root, "ancestor-secret.txt");
        await File.WriteAllTextAsync(secret, "ancestor-secret-content", Token);
        var closedAcl = new DirectoryInfo(_closed).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();

        var metadata = await RunAsync(WindowsPowerShell($"[IO.File]::GetAttributes('{_root}')"), Policy);
        metadata.ExitCode.ShouldBe(0, metadata.Stderr);
        metadata.Stdout.ShouldContain("Directory");

        var listing = await RunAsync(WindowsPowerShell($"[IO.Directory]::GetFileSystemEntries('{_root}')"), Policy);
        listing.ExitCode.ShouldNotBe(0);
        listing.Stdout.ShouldNotContain("ancestor-secret.txt");

        var content = await RunAsync(WindowsPowerShell($"[IO.File]::ReadAllText('{secret}')"), Policy);
        content.ExitCode.ShouldNotBe(0);
        content.Stdout.ShouldNotContain("ancestor-secret-content");

        var sibling = await RunAsync(WindowsPowerShell($"[IO.File]::ReadAllText('{Path.Combine(_closed, "secret.txt")}')"), Policy);
        sibling.ExitCode.ShouldNotBe(0);
        sibling.Stdout.ShouldNotContain("secret-content");
        new DirectoryInfo(_closed).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm().ShouldBe(closedAcl);
    }

    [Fact]
    public async Task WindowsFileGrantsAllowParentMetadataWithoutOpeningSiblingFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        Shell.RequireSandbox(_launcher);
        var file = Path.Combine(_readable, "open.txt");
        var sibling = Path.Combine(_readable, "sibling.txt");
        await File.WriteAllTextAsync(sibling, "sibling-secret", Token);
        var policy = new SandboxPolicy([new(file, SandboxAccess.Read), new(_writable, SandboxAccess.ReadWrite)]);

        var metadata = await RunAsync(WindowsPowerShell($"[IO.File]::GetAttributes('{_readable}')"), policy);
        metadata.ExitCode.ShouldBe(0, metadata.Stderr);
        metadata.Stdout.ShouldContain("Directory");
        var content = await RunAsync(WindowsPowerShell($"[IO.File]::ReadAllText('{file}')"), policy);
        content.ExitCode.ShouldBe(0, content.Stderr);
        content.Stdout.ShouldContain("open-content");

        var denied = await RunAsync(WindowsPowerShell($"[IO.File]::ReadAllText('{sibling}')"), policy);
        denied.ExitCode.ShouldNotBe(0);
        denied.Stdout.ShouldNotContain("sibling-secret");
    }

    [Fact]
    public async Task WindowsAncestorMetadataCapabilitiesDoNotLeakBetweenPolicies()
    {
        if (!OperatingSystem.IsWindows()) return;
        Shell.RequireSandbox(_launcher);
        var nested = Directory.CreateDirectory(Path.Combine(_closed, "nested")).FullName;
        var opening = new SandboxPolicy([new(nested, SandboxAccess.Read), new(_writable, SandboxAccess.ReadWrite)]);
        var inspect = WindowsPowerShell($"[IO.File]::GetAttributes('{_closed}')");

        var allowed = await RunAsync(inspect, opening);
        allowed.ExitCode.ShouldBe(0, allowed.Stderr);
        allowed.Stdout.ShouldContain("Directory");
        var denied = await RunAsync(inspect, Policy);
        denied.ExitCode.ShouldNotBe(0);
    }

    private ProcessStartInfo WindowsPowerShell(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            WorkingDirectory = _writable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; " + command })
            start.ArgumentList.Add(argument);
        return start;
    }

    [Fact]
    public async Task AnUnconfinedProgramReadsWhatAConfinedOneCannot()
    {
        var result = await RunAsync(Command(Shell.Read(Path.Combine(_closed, "secret.txt"))), policy: null);

        result.ExitCode.ShouldBe(0, result.Stderr);
        result.Stdout.ShouldContain("secret-content");
    }

    [Fact]
    public async Task AConfinedProgramWritesADirectoryThePolicyOpensForWriting()
    {
        Shell.RequireSandbox(_launcher);
        var file = Path.Combine(_writable, "written.txt");

        var result = await RunAsync(Command(Shell.Write(file, "written")), Policy);

        result.ExitCode.ShouldBe(0, result.Stderr);
        (await File.ReadAllTextAsync(file, Token)).ShouldContain("written");
    }

    [Fact]
    public async Task AConfinedProgramCannotWriteADirectoryOpenOnlyForReading()
    {
        Shell.RequireSandbox(_launcher);
        var file = Path.Combine(_readable, "written.txt");

        var result = await RunAsync(Command(Shell.Write(file, "written")), Policy);

        result.ExitCode.ShouldNotBe(0);
        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public async Task AConfinedProgramCannotWriteOutsideThePolicy()
    {
        Shell.RequireSandbox(_launcher);
        var file = Path.Combine(_closed, "written.txt");

        var result = await RunAsync(Command(Shell.Write(file, "written")), Policy);

        result.ExitCode.ShouldNotBe(0);
        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public async Task ChildrenOfAConfinedProgramStayConfined()
    {
        Shell.RequireSandbox(_launcher);

        var result = await RunAsync(Command(Shell.Nested(Shell.Read(Path.Combine(_closed, "secret.txt")))), Policy);

        result.Stdout.ShouldNotContain("secret-content");
    }

    [Fact]
    public async Task AConfinedProgramKeepsItsExitCodeWorkingDirectoryAndEnvironment()
    {
        Shell.RequireSandbox(_launcher);
        var start = Shell.Command($"{Shell.PrintDirectory()} && {Shell.PrintVariable("AI_SANDBOX_PROBE")} && {Shell.Exit(5)}", _writable);
        start.Environment["AI_SANDBOX_PROBE"] = "probe-value";

        var result = await RunAsync(start, Policy);

        result.ExitCode.ShouldBe(5, result.Stderr);
        result.Stdout.ShouldContain(Path.GetFileName(_writable));
        result.Stdout.ShouldContain("probe-value");
    }

    [Fact]
    public async Task AConfinedProgramReceivesItsStandardInput()
    {
        Shell.RequireSandbox(_launcher);
        var start = Command(OperatingSystem.IsWindows() ? "more" : "cat");

        using var launched = _launcher.Start(new ProcessLaunch(start, Policy, TimeoutMs: 30000));
        var output = launched.StandardOutput.ReadToEndAsync(Token);
        await launched.StandardInput.WriteLineAsync("piped-line");
        launched.StandardInput.Close();
        await launched.WaitForExitAsync(Token);

        (await output).ShouldContain("piped-line");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheDeadlineKillsTheTree(bool confined)
    {
        if (confined) Shell.RequireSandbox(_launcher);
        var timer = Stopwatch.StartNew();

        var result = await RunAsync(Command(Shell.Sleep()), confined ? Policy : null, timeoutMs: 500);

        result.TimedOut.ShouldBeTrue();
        timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CancellationKillsTheTreeAndThrows()
    {
        using var cancellation = new CancellationTokenSource(500);
        using var launched = _launcher.Start(new ProcessLaunch(Command(Shell.Sleep())));

        await Should.ThrowAsync<OperationCanceledException>(() => launched.WaitForExitAsync(cancellation.Token));

        launched.HasExited.ShouldBeTrue();
        launched.TimedOut.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingProgramFailsTheStart(bool confined)
    {
        if (confined) Shell.RequireSandbox(_launcher);
        var start = new ProcessStartInfo(Path.Combine(_root, "no-such-program")) { UseShellExecute = false };

        Should.Throw<Win32Exception>(() => _launcher.Start(new ProcessLaunch(start, confined ? Policy : null)));
    }

    [Fact]
    public void TheShellCannotBeUsed()
    {
        var start = new ProcessStartInfo("anything") { UseShellExecute = true };

        Should.Throw<ArgumentException>(() => _launcher.Start(new ProcessLaunch(start, Policy)));
    }

    [Fact]
    public async Task TheProcessLimitIsReportedOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Process limits are applied through a Windows job object.");

        var result = await RunAsync(Command(Shell.Nested(Shell.Exit(0))), policy: null,
            limits: ChildProcessLimits.Create(memoryLimitMb: null, processLimit: 1));

        result.LimitExceeded.ShouldBe(ChildProcessJob.Processes);
    }

    /// <summary>
    /// The race the Windows start closes: a program that starts a child the moment it runs. Started
    /// first and put in its job after, such a child escaped the job now and then, so it is tried
    /// many times; created inside the job, every child counts, confined or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AChildStartedAtOnceIsAlwaysInsideTheJob(bool confined)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Process limits are applied through a Windows job object.");
        if (confined) Shell.RequireSandbox(_launcher);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await RunAsync(Command(Shell.Nested(Shell.Exit(0))), confined ? Policy : null,
                limits: ChildProcessLimits.Create(memoryLimitMb: null, processLimit: confined ? 2 : 1));

            result.LimitExceeded.ShouldBe(ChildProcessJob.Processes, $"attempt {attempt + 1}");
        }
    }

    [Fact]
    public void TheSupportIsReportedForThisMachine()
    {
        var support = _launcher.Support;

        support.Mechanism.ShouldNotBeNullOrWhiteSpace();
        if (Environment.GetEnvironmentVariable(Shell.RequiredVariable) == "1")
        {
            support.Available.ShouldBeTrue(support.Reason);
        }
    }

    /// <summary>
    /// A shell command that runs in the writable directory: a confined program cannot even start in
    /// a directory the policy does not open, which is not what these tests are about.
    /// </summary>
    /// <summary>
    /// What the support reports about the null device is what a confined program meets: Windows 10
    /// resets the device's security at boot without the AppContainers, Windows 11 opens it to them.
    /// </summary>
    [Fact]
    public async Task TheSupportSaysWhetherAConfinedProgramMayOpenTheNullDevice()
    {
        Shell.RequireSandbox(_launcher);
        if (!OperatingSystem.IsWindows())
        {
            _launcher.Support.OpensNullDevice.ShouldBeTrue();
            return;
        }

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            WorkingDirectory = _writable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("echo.>nul");

        var result = await RunAsync(start, Policy);

        (result.ExitCode == 0).ShouldBe(_launcher.Support.OpensNullDevice, result.Stderr);
    }

    private ProcessStartInfo Command(string command) => Shell.Command(command, _writable);

    private async Task<Result> RunAsync(ProcessStartInfo start, SandboxPolicy? policy, int timeoutMs = 30000, ChildProcessLimits? limits = null)
    {
        using var launched = _launcher.Start(new ProcessLaunch(start, policy, limits, timeoutMs));
        launched.StandardInput.Close();
        var stdout = launched.StandardOutput.ReadToEndAsync(Token);
        var stderr = launched.StandardError.ReadToEndAsync(Token);
        await launched.WaitForExitAsync(Token);
        return new Result(launched.ExitCode, await stdout, await stderr, launched.TimedOut, launched.LimitExceeded);
    }

    private sealed record Result(int ExitCode, string Stdout, string Stderr, bool TimedOut, string? LimitExceeded);
}
