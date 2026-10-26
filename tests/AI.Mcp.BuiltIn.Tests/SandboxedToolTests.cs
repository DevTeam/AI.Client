using System.Text.Json;
using AI.Sandbox;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;

namespace AI.Mcp.BuiltIn.Tests;

/// <summary>
/// Starts the built-in server the way the Host does: directory grants in its environment. Then <c>process_run</c> and <c>cs_run</c> must
/// reach exactly the granted directories: read-only grants for reading, writable grants and the
/// chat's temporary directory for writing, and nothing else.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SandboxedToolTests : IAsyncLifetime
{
    private const string ProtocolVersion = "2026-07-28";

    private static readonly string[] ReadOnly = ["read"];
    private static readonly string[] Everything = ["read", "write", "edit", "delete"];

    private readonly string _root = Directory.CreateTempSubdirectory("ai-builtin-sandbox-").FullName;
    private string _readable = "";
    private string _writable = "";
    private string _temporary = "";
    private string _closed = "";
    private McpClient _client = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _readable = Directory.CreateDirectory(Path.Combine(_root, "readable")).FullName;
        _writable = Directory.CreateDirectory(Path.Combine(_root, "writable")).FullName;
        _temporary = Directory.CreateDirectory(Path.Combine(_root, "temporary")).FullName;
        _closed = Directory.CreateDirectory(Path.Combine(_root, "closed")).FullName;
        await File.WriteAllTextAsync(Path.Combine(_readable, "open.txt"), "open-content", Token);
        await File.WriteAllTextAsync(Path.Combine(_closed, "secret.txt"), "secret-content", Token);

        var grants = JsonSerializer.Serialize(new object[]
        {
            new { root = _readable, recursive = true, capabilities = ReadOnly, purpose = "project" },
            new { root = _writable, recursive = true, capabilities = Everything, purpose = "project" },
            new { root = _temporary, recursive = true, capabilities = Everything, purpose = "chatTemporary" }
        });
        _client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Default tools",
                Command = "dotnet",
                Arguments = [Path.Combine(AppContext.BaseDirectory, "AI.Mcp.BuiltIn.dll")],
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["AI_CLIENT_DIRECTORY_GRANTS"] = grants
                },
                ShutdownTimeout = TimeSpan.FromMilliseconds(500)
            }),
            new McpClientOptions
            {
                ProtocolVersion = ProtocolVersion,
                DiscoverProbeTimeout = TimeSpan.FromSeconds(30)
            },
            cancellationToken: Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        // A worker the server just killed lets go of its directories a moment after the kill.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 20)
            {
                await Task.Delay(250);
            }
        }
    }

    [Fact]
    public async Task TheToolsSayTheyAreSandboxed()
    {
        RequireSandbox();
        var tools = await _client.ListToolsAsync(cancellationToken: Token);

        tools.Single(tool => tool.Name == "process_run").Description.ShouldNotBeNull().ShouldContain("sandbox");
        tools.Single(tool => tool.Name == "cs_run").Description.ShouldNotBeNull().ShouldContain("sandbox");
    }

    [Theory]
    [InlineData("process_run")]
    [InlineData("process_run_unrestricted")]
    public async Task ManagedCommandsContinueOverStdioWithoutRelaunching(string tool)
    {
        if (tool == "process_run") RequireSandbox();
        var release = Path.Combine(_writable, "release");
        var launches = Path.Combine(_writable, "launches");
        var (shell, arguments) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                $"[IO.File]::AppendAllText('{launches}', '1'); [Console]::Write('ready'); while (!(Test-Path -LiteralPath '{release}')) {{ Start-Sleep -Milliseconds 50 }}; [Console]::Write('done'); exit 7"
            })
            : ("/bin/sh", new[] { "-c", $"printf 1 >> '{launches}'; printf ready; while [ ! -f '{release}' ]; do sleep 0.05; done; printf done; exit 7" });
        var started = await CallAsync(tool, new Dictionary<string, object?>
        {
            ["executable"] = shell, ["arguments"] = arguments, ["workingDirectory"] = _writable,
            ["timeoutMs"] = 120000, ["waitTimeoutMs"] = 0
        });
        started.GetProperty("status").GetString().ShouldBe("running");
        var id = started.GetProperty("processId").GetString();
        var page = await CallAsync("process_wait", new Dictionary<string, object?>
        {
            ["processId"] = id, ["waitTimeoutMs"] = 50
        });
        page.GetProperty("status").GetString().ShouldBe("running");
        await File.WriteAllTextAsync(release, "go", Token);
        var final = await CallAsync("process_wait", new Dictionary<string, object?>
        {
            ["processId"] = id, ["waitTimeoutMs"] = 60000
        });
        final.GetProperty("status").GetString().ShouldBe("completed", final.ToString());
        final.GetProperty("exitCode").GetInt32().ShouldBe(7);
        final.GetProperty("stdout").GetString().ShouldBe("readydone");
        (await File.ReadAllTextAsync(launches, Token)).ShouldBe("1");
        var log = final.GetProperty("stdoutPath").GetString()!;
        log.ShouldStartWith(_temporary);
        (await File.ReadAllTextAsync(log, Token)).ShouldBe("readydone");
        if (tool == "process_run") final.GetProperty("sandbox").GetString().ShouldNotBeNullOrWhiteSpace();
        else final.GetProperty("sandbox").ValueKind.ShouldBe(JsonValueKind.Null);
        var stopped = await CallAsync("process_stop", new Dictionary<string, object?> { ["processId"] = id });
        stopped.GetProperty("exitCode").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task ACommandReadsAReadOnlyGrantButNotAnUngrantedDirectory()
    {
        RequireSandbox();

        var open = await RunCommandAsync(Read(Path.Combine(_readable, "open.txt")));
        var closed = await RunCommandAsync(Read(Path.Combine(_closed, "secret.txt")));

        open.GetProperty("stdout").GetString().ShouldNotBeNull().ShouldContain("open-content", Case.Sensitive, open.ToString());
        open.GetProperty("sandbox").GetString().ShouldNotBeNullOrWhiteSpace();
        closed.GetProperty("exitCode").GetInt32().ShouldNotBe(0);
        closed.GetProperty("stdout").GetString().ShouldNotBeNull().ShouldNotContain("secret-content");
    }

    [Fact]
    public async Task ACommandWritesOnlyWhereAGrantAllowsWriting()
    {
        RequireSandbox();

        var allowed = await RunCommandAsync(Write(Path.Combine(_writable, "out.txt")));
        var readOnly = await RunCommandAsync(Write(Path.Combine(_readable, "out.txt")));
        var closed = await RunCommandAsync(Write(Path.Combine(_closed, "out.txt")));

        allowed.GetProperty("exitCode").GetInt32().ShouldBe(0, allowed.ToString());
        File.Exists(Path.Combine(_writable, "out.txt")).ShouldBeTrue();
        readOnly.GetProperty("exitCode").GetInt32().ShouldNotBe(0);
        File.Exists(Path.Combine(_readable, "out.txt")).ShouldBeFalse();
        closed.GetProperty("exitCode").GetInt32().ShouldNotBe(0);
        File.Exists(Path.Combine(_closed, "out.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task AScriptReadsAndWritesTheGrantsAndNothingElse()
    {
        RequireSandbox();

        var result = await RunScriptAsync(
            """
            string Try(Func<string> action) { try { return action(); } catch (Exception error) { return error.GetType().Name; } }
            var open = Try(() => File.ReadAllText(Path.Combine(Args[0], "open.txt")));
            var secret = Try(() => File.ReadAllText(Path.Combine(Args[2], "secret.txt")));
            var written = Try(() => { File.WriteAllText(Path.Combine(Args[1], "script.txt"), "script"); return "written"; });
            var readOnly = Try(() => { File.WriteAllText(Path.Combine(Args[0], "script.txt"), "script"); return "written"; });
            var outside = Try(() => { File.WriteAllText(Path.Combine(Args[2], "script.txt"), "script"); return "written"; });
            $"{open}|{secret}|{written}|{readOnly}|{outside}"
            """,
            _readable, _writable, _closed);

        result.GetProperty("success").GetBoolean().ShouldBeTrue(result.ToString());
        result.GetProperty("sandbox").GetString().ShouldNotBeNullOrWhiteSpace();
        var parts = result.GetProperty("returnValue").GetString()!.Split('|');
        parts[0].ShouldBe("open-content");
        parts[1].ShouldNotBe("secret-content");
        parts[2].ShouldBe("written");
        parts[3].ShouldNotBe("written");
        parts[4].ShouldNotBe("written");
        File.Exists(Path.Combine(_closed, "script.txt")).ShouldBeFalse();
    }

    /// <summary>
    /// Elsewhere the temporary directory is the chat's, handed over through the environment; on
    /// Windows an AppContainer always gets the container's own, whatever the environment says.
    /// Either way a script can write its temporary files.
    /// </summary>
    [Fact]
    public async Task AScriptCanWriteItsTemporaryDirectory()
    {
        RequireSandbox();

        var result = await RunScriptAsync("var file = Path.Combine(Path.GetTempPath(), \"probe.txt\"); File.WriteAllText(file, \"t\"); File.Delete(file); Path.GetTempPath()");

        result.GetProperty("success").GetBoolean().ShouldBeTrue(result.ToString());
        var temporary = Path.TrimEndingDirectorySeparator(result.GetProperty("returnValue").GetString()!);
        if (!OperatingSystem.IsWindows())
        {
            temporary.ShouldBe(Path.TrimEndingDirectorySeparator(_temporary));
        }
    }

    /// <summary>
    /// A sandboxed script runs without asking, so it must not be able to send what it read
    /// anywhere: where the OS can close the network, a connection to a listener on this machine is
    /// refused.
    /// </summary>
    [Fact]
    public async Task AScriptCannotOpenANetworkConnection()
    {
        RequireSandbox();
        if (!new ProcessLauncher().Support.ClosesNetwork) Assert.Skip("This kernel cannot close the network to a sandboxed process.");
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var result = await RunScriptAsync(
            """
            string Probe()
            {
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    client.Connect("127.0.0.1", int.Parse(Args[0]));
                    return "connected";
                }
                catch (Exception error)
                {
                    return error.GetType().Name;
                }
            }
            Probe()
            """,
            port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        result.GetProperty("success").GetBoolean().ShouldBeTrue(result.ToString());
        result.GetProperty("returnValue").GetString().ShouldNotBe("connected");
    }

    [Fact]
    public async Task ConsecutiveScriptsRunInFreshWorkers()
    {
        RequireSandbox();

        var first = await RunScriptAsync("Environment.ProcessId");
        var second = await RunScriptAsync("Environment.ProcessId");

        first.GetProperty("success").GetBoolean().ShouldBeTrue(first.ToString());
        second.GetProperty("success").GetBoolean().ShouldBeTrue(second.ToString());
        second.GetProperty("returnValue").GetString().ShouldNotBe(first.GetProperty("returnValue").GetString());
    }

    private static void RequireSandbox()
    {
        var support = new ProcessLauncher().Support;
        if (support.Available) return;
        Assert.False(Environment.GetEnvironmentVariable("AI_SANDBOX_REQUIRED") == "1", support.Reason);
        Assert.Skip($"{support.Mechanism} is not available: {support.Reason}");
    }

    // cmd does not split its command line the way the C runtime quotes it, so Windows paths go unquoted.
    private static string Read(string file) => OperatingSystem.IsWindows() ? $"type {file}" : $"cat '{file}'";

    private static string Write(string file) => OperatingSystem.IsWindows() ? $"echo data> {file}" : $"echo data > '{file}'";

    private async Task<JsonElement> RunCommandAsync(string command)
    {
        var (shell, arguments) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/d", "/c", command })
            : ("/bin/sh", new[] { "-c", command });
        return await CallAsync("process_run", new Dictionary<string, object?>
        {
            ["executable"] = shell,
            ["arguments"] = arguments,
            ["workingDirectory"] = _writable,
            ["timeoutMs"] = 60000
        });
    }

    private Task<JsonElement> RunScriptAsync(string code, params string[] arguments) =>
        CallAsync("cs_run", new Dictionary<string, object?>
        {
            ["code"] = code,
            ["arguments"] = arguments,
            ["workingDirectory"] = _writable,
            ["timeoutMs"] = 120000
        });

    private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(tool, arguments, cancellationToken: Token);
        result.StructuredContent.ShouldNotBeNull();
        return result.StructuredContent!.Value;
    }
}
