using AI.Client.Mcp.BuiltIn;
using AI.Client.Mcp.BuiltIn.Grants;
using AI.Client.Mcp.BuiltIn.Process;
using AI.Client.Mcp.BuiltIn.Web;

namespace AI.Client.Infrastructure.Tests.Tools;

using AI.Client.Application.Tools;
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
        await using var session = await new DefaultToolSessionFactory().OpenAsync([], timeout.Token);
        session.Tools.Select(item => item.OriginalName).ShouldBe(
        [
            "process_run", "fetch", "list_allowed_directories", "read_text_file", "read_multiple_files", "list_directory",
            "directory_tree", "search_files", "get_file_info", "write_file", "edit_file", "create_directory", "move_file"
        ], ignoreOrder: true);
        var tool = session.Tools.Single(item => item.OriginalName == "process_run");
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

    [Fact]
    public void ShouldDenyFileSystemAccessWithoutGrant()
    {
        var guard = new PathGuard(new Grants());
        guard.Grants.ShouldBeEmpty();
        Should.Throw<GrantException>(() => guard.Resolve(AppContext.BaseDirectory, GrantCapability.Read));
    }

    [Fact]
    public void ShouldEnforceCapabilitiesAndContainment()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-guard").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var guard = new PathGuard(new Grants(new DirectoryGrantSpec(root, true, new HashSet<GrantCapability> { GrantCapability.Read })));

            guard.Resolve(Path.Combine(nested, "file.txt"), GrantCapability.Read).ShouldBe(Path.Combine(nested, "file.txt"));
            Should.Throw<GrantException>(() => guard.Resolve(Path.Combine(root, "file.txt"), GrantCapability.Write));
            Should.Throw<GrantException>(() => guard.Resolve(Path.Combine(root, "..", "outside.txt"), GrantCapability.Read));
            Should.Throw<GrantException>(() => guard.Resolve("relative.txt", GrantCapability.Read));

            var shallow = new PathGuard(new Grants(new DirectoryGrantSpec(root, false, new HashSet<GrantCapability> { GrantCapability.Read })));
            shallow.Resolve(Path.Combine(root, "file.txt"), GrantCapability.Read).ShouldNotBeNullOrEmpty();
            Should.Throw<GrantException>(() => shallow.Resolve(Path.Combine(nested, "file.txt"), GrantCapability.Read));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ShouldResolveGrantRootsFromEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(EnvironmentGrantSource.Variable);
        try
        {
            Environment.SetEnvironmentVariable(EnvironmentGrantSource.Variable, JsonSerializer.Serialize(new[]
            {
                new { root = AppContext.BaseDirectory, recursive = true, capabilities = (string[])["read", "write", "nonsense"] },
                new { root = "relative", recursive = true, capabilities = (string[])["read"] },
                new { root = AppContext.BaseDirectory, recursive = true, capabilities = (string[])[] }
            }));
            var grant = new PathGuard(new EnvironmentGrantSource()).Grants.ShouldHaveSingleItem();
            grant.Capabilities.ShouldBe(new HashSet<GrantCapability> { GrantCapability.Read, GrantCapability.Write });

            Environment.SetEnvironmentVariable(EnvironmentGrantSource.Variable, "{ not json");
            new PathGuard(new EnvironmentGrantSource()).Grants.ShouldBeEmpty();
        }
        finally { Environment.SetEnvironmentVariable(EnvironmentGrantSource.Variable, previous); }
    }

    [Fact]
    public void ShouldExtractMarkdownFromHtml()
    {
        var markdown = new HtmlText().ToMarkdown(
            "<html><head><title>t</title><style>body{}</style></head><body><nav>skip</nav>"
            + "<main><h1>Title</h1><p>Hello &amp; welcome</p><ul><li>one</li><li><a href=\"https://example.com\">two</a></li></ul>"
            + "<script>alert(1)</script></main></body></html>");
        markdown.ShouldContain("# Title");
        markdown.ShouldContain("Hello & welcome");
        markdown.ShouldContain("- one");
        markdown.ShouldContain("[two](https://example.com)");
        markdown.ShouldNotContain("alert");
        markdown.ShouldNotContain("body{}");
        markdown.ShouldNotContain("<");
    }

    [Fact]
    public async Task ShouldSkipDefaultExcludedDirectoriesUnlessDisabled()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-tree-excludes").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "keep.txt"), "keep");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/master");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var session = await new DefaultToolSessionFactory().OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], timeout.Token);
            var token = timeout.Token;

            var defaultTree = await Structured(session, "directory_tree", new { path = root }, token);
            var defaultTreePaths = defaultTree.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetProperty("path").GetString()).ToArray();
            defaultTreePaths.ShouldNotContain(entry => entry!.Contains(".git", StringComparison.Ordinal));
            defaultTreePaths.ShouldContain(Path.Combine("src", "keep.txt"));

            var fullTree = await Structured(session, "directory_tree", new { path = root, excludeDefaults = false }, token);
            fullTree.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetProperty("path").GetString())
                .ShouldContain(entry => entry!.Contains(".git", StringComparison.Ordinal));

            var defaultSearch = await Structured(session, "search_files", new { path = root, pattern = "*" }, token);
            defaultSearch.GetProperty("matches").EnumerateArray().Select(item => item.GetString())
                .ShouldNotContain(match => match!.Contains(".git", StringComparison.Ordinal));

            var fullSearch = await Structured(session, "search_files", new { path = root, pattern = "*", excludeDefaults = false }, token);
            fullSearch.GetProperty("matches").EnumerateArray().Select(item => item.GetString())
                .ShouldContain(match => match!.Contains(".git", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    // Reproduces the incident this guards against: a single directory_tree call whose entry
    // count was well within its (much higher) count cap still produced ~1.4MB of doubly
    // serialized JSON, large enough that the model's endpoint rejected the next turn outright.
    // Names are padded to a fixed, generous length so the truncation point is dominated by the
    // name itself rather than by how long the OS's temp path happens to be on the machine running
    // the test, and stays well under the pre-existing per-tool entry-count caps (20000 / 5000 /
    // 1000) — proving these are new, size-based truncations, not the old count-based ones.
    [Fact]
    public async Task ShouldTruncateResultsOnTotalSizeBelowTheEntryCountCaps()
    {
        const int fileCount = 1500;
        var root = Directory.CreateTempSubdirectory("ai-client-tree-budget").FullName;
        try
        {
            for (var index = 0; index < fileCount; index++)
            {
                var name = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture).PadRight(116, 'x') + ".txt";
                File.WriteAllBytes(Path.Combine(root, name), []);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using var session = await new DefaultToolSessionFactory().OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], timeout.Token);
            var token = timeout.Token;

            var tree = await Structured(session, "directory_tree", new { path = root }, token);
            tree.GetProperty("truncated").GetBoolean().ShouldBeTrue();
            tree.GetProperty("entries").GetArrayLength().ShouldBeInRange(1, fileCount - 1);

            var list = await Structured(session, "list_directory", new { path = root }, token);
            list.GetProperty("truncated").GetBoolean().ShouldBeTrue();
            list.GetProperty("entries").GetArrayLength().ShouldBeInRange(1, fileCount - 1);

            var search = await Structured(session, "search_files", new { path = root, pattern = "*.txt" }, token);
            search.GetProperty("truncated").GetBoolean().ShouldBeTrue();
            // Below the tool's own 1000-match count cap: without the size cap, this fixture would
            // have hit exactly that count cap instead.
            search.GetProperty("matches").GetArrayLength().ShouldBeInRange(1, 999);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldReadWriteAndEditWithinGrantOverStdio()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-files").FullName;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var session = await new DefaultToolSessionFactory().OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read", "write", "edit", "delete"])], timeout.Token);
            var token = timeout.Token;

            (await Structured(session, "list_allowed_directories", new { }, token))
                .GetProperty("directories").EnumerateArray().Single()
                .GetProperty("capabilities").EnumerateArray().Select(item => item.GetString())
                .ShouldBe(["delete", "edit", "read", "write"]);

            var file = Path.Combine(root, "src", "sample.txt");
            (await Structured(session, "create_directory", new { path = Path.Combine(root, "src") }, token))
                .GetProperty("created").GetBoolean().ShouldBeTrue();
            (await Structured(session, "write_file", new { path = file, content = "alpha\nbeta\n" }, token))
                .GetProperty("created").GetBoolean().ShouldBeTrue();
            (await Structured(session, "read_text_file", new { path = file }, token))
                .GetProperty("content").GetString().ShouldBe("alpha\nbeta\n");
            (await Structured(session, "read_text_file", new { path = file, tail = 1 }, token))
                .GetProperty("content").GetString().ShouldBe("beta");

            var edits = new[] { new { oldText = "beta", newText = "gamma" } };
            (await Structured(session, "edit_file", new { path = file, edits, dryRun = true }, token))
                .GetProperty("diff").GetString()!.ShouldContain("-beta");
            (await Structured(session, "read_text_file", new { path = file }, token))
                .GetProperty("content").GetString()!.ShouldContain("beta");
            (await Structured(session, "edit_file", new { path = file, edits }, token))
                .GetProperty("applied").GetInt32().ShouldBe(1);
            (await Structured(session, "read_text_file", new { path = file }, token))
                .GetProperty("content").GetString()!.ShouldContain("gamma");
            (await Structured(session, "edit_file", new { path = file, edits }, token))
                .GetProperty("error").GetString()!.ShouldContain("was not found");

            (await Structured(session, "read_multiple_files", new { paths = (string[])[file] }, token))
                .GetProperty("files").EnumerateArray().Single().GetProperty("content").GetString()!.ShouldContain("gamma");
            (await Structured(session, "search_files", new { path = root, pattern = "*.txt" }, token))
                .GetProperty("matches").EnumerateArray().Single().GetString().ShouldBe(file);
            (await Structured(session, "list_directory", new { path = root }, token))
                .GetProperty("entries").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("src");
            (await Structured(session, "directory_tree", new { path = root }, token))
                .GetProperty("entries").GetArrayLength().ShouldBe(2);
            (await Structured(session, "get_file_info", new { path = file }, token))
                .GetProperty("kind").GetString().ShouldBe("file");

            var moved = Path.Combine(root, "moved.txt");
            (await Structured(session, "move_file", new { source = file, destination = moved }, token))
                .GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
            File.Exists(moved).ShouldBeTrue();

            var outside = Path.Combine(Path.GetTempPath(), "ai-client-outside.txt");
            (await Structured(session, "write_file", new { path = outside, content = "no" }, token))
                .GetProperty("error").GetString()!.ShouldContain("No directory grant");
            File.Exists(outside).ShouldBeFalse();

            var relative = session.Tools.Single(item => item.OriginalName == "write_file");
            Should.Throw<ArgumentException>(() => session.ValidateArguments(relative, "{\"path\":\"relative.txt\",\"content\":\"x\"}"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<JsonElement> Structured(IToolSession session, string name, object arguments, CancellationToken token)
    {
        var tool = session.Tools.Single(item => item.OriginalName == name);
        var result = await session.CallAsync(tool, JsonSerializer.Serialize(arguments), token);
        return JsonDocument.Parse(result).RootElement.GetProperty("structuredContent").Clone();
    }

    private sealed class Grants(params DirectoryGrantSpec[] grants) : IGrantSource
    {
        public IReadOnlyList<DirectoryGrantSpec> Load() => grants;
    }
}
