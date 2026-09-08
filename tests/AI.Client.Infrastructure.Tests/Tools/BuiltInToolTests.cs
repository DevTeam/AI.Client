using AI.Client.Mcp.BuiltIn;

namespace AI.Client.Infrastructure.Tests.Tools;

using AI.Client.Infrastructure.Tools;
using Shouldly;
using System.Text.Json;
using Xunit;

public sealed class BuiltInToolTests
{
    [Fact]
    public async Task ShouldKillDescendantsWhenTheirParentExits()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await new ProcessRunner().RunAsync(new ProcessRequest("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "$child = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30'; $child.Id"],
            AppContext.BaseDirectory, 10000), CancellationToken.None);
        result.ExitCode.ShouldBe(0);
        var pid = int.Parse(result.Stdout.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        System.Diagnostics.Process child;
        try { child = System.Diagnostics.Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (child)
        {
            try { await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        }
    }

    [Fact]
    public async Task ShouldNotInheritArbitraryHostEnvironment()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string variable = "AI_CLIENT_PROCESS_TEST_SECRET";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "test-value");
        try
        {
            var result = await new ProcessRunner().RunAsync(new ProcessRequest("powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Write([Environment]::GetEnvironmentVariable('AI_CLIENT_PROCESS_TEST_SECRET'))"],
                AppContext.BaseDirectory), CancellationToken.None);
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldBeEmpty();
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }
    [Fact]
    public async Task ShouldDiscoverValidateAndRunOverStdio()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = await new DefaultToolSessionFactory().OpenAsync(timeout.Token);
        var tool = session.Tools.ShouldHaveSingleItem();
        tool.OriginalName.ShouldBe("process_run");
        tool.SchemaHash.Length.ShouldBe(64);
        var arguments = JsonSerializer.Serialize(new { executable = "dotnet", arguments = (string[])["--info"], workingDirectory = AppContext.BaseDirectory });
        var result = await session.CallAsync(tool, arguments, timeout.Token);
        using var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("structuredContent").GetProperty("exitCode").GetInt32().ShouldBe(0);
        json.RootElement.GetProperty("structuredContent").GetProperty("stdout").GetString().ShouldNotBeNullOrWhiteSpace();
        var canonical = session.ValidateArguments(tool, "{\"executable\":\"dotnet\"}");
        using var canonicalJson = JsonDocument.Parse(canonical);
        canonicalJson.RootElement.GetProperty("executable").GetString().ShouldBe("dotnet");
        canonicalJson.RootElement.TryGetProperty("workingDirectory", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldReportMissingExecutable()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRequest("nonexistent-ai-client-test-command", [], AppContext.BaseDirectory), CancellationToken.None);
        result.ExitCode.ShouldBeNull();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ShouldCaptureBothStreamsAndNonZeroExitWithoutDeadlock()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await new ProcessRunner().RunAsync(new ProcessRequest("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write(('x' * 100000)); [Console]::Error.Write(('y' * 100000)); exit 7"], AppContext.BaseDirectory), CancellationToken.None);
        result.ExitCode.ShouldBe(7);
        result.Stdout.Length.ShouldBe(ProcessRunner.OutputLimit);
        result.Stderr.Length.ShouldBe(ProcessRunner.OutputLimit);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldTimeoutAndHonorCancellation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var request = new ProcessRequest("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], AppContext.BaseDirectory, 300);
        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);
        result.TimedOut.ShouldBeTrue();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Should.ThrowAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(request with { TimeoutMs = 120000 }, cancel.Token));
    }
}
