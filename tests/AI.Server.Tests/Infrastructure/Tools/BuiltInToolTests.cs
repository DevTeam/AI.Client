using AI.Mcp.BuiltIn;
using AI.Mcp.BuiltIn.Grants;
using AI.Mcp.BuiltIn.Process;
using AI.Mcp.BuiltIn.Web;

namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Tools;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
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
        await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync([], ToolRunContext.None, timeout.Token);
        session.Tools.Select(item => item.OriginalName).ShouldBe(
        [
            "process_run", "fetch", "list_allowed_directories", "read_text_file", "read_multiple_files", "list_directory",
            "directory_tree", "search_files", "grep_files", "get_file_info", "write_file", "edit_file", "create_directory",
            "move_file", "delete_file", "delete_directory", "zip_list", "zip_read", "zip_extract", "zip_create"
        ], ignoreOrder: true);
        var tool = session.Tools.Single(item => item.OriginalName == "process_run");
        tool.SchemaHash.Length.ShouldBe(64);
        var timeoutSchema = tool.ModelDefinition.InputSchema.GetProperty("properties").GetProperty("timeoutMs");
        timeoutSchema.GetProperty("default").GetInt32().ShouldBe(600000);
        timeoutSchema.GetProperty("maximum").GetInt32().ShouldBe(600000);
        var arguments = JsonSerializer.Serialize(new { executable = "dotnet", arguments = (string[])["--info"], workingDirectory = AppContext.BaseDirectory, timeoutMs = 600000 });
        session.ValidateArguments(tool, arguments).ShouldNotBeNullOrWhiteSpace();
        Should.Throw<ArgumentException>(() => session.ValidateArguments(tool,
            JsonSerializer.Serialize(new { executable = "dotnet", timeoutMs = 600001 })));
        var result = await session.CallAsync(tool, arguments, null, timeout.Token);
        result.IsError.ShouldBeFalse();
        result.StructuredContent.ShouldNotBeNull();
        result.StructuredContent!.Value.GetProperty("exitCode").GetInt32().ShouldBe(0);
        result.StructuredContent!.Value.GetProperty("stdout").GetString().ShouldNotBeNullOrWhiteSpace();
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
        await Should.ThrowAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(request with { TimeoutMs = 600000 }, cancel.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600001)]
    public async Task ShouldRejectProcessTimeoutOutsideSupportedRange(int timeoutMs)
    {
        await Should.ThrowAsync<ArgumentException>(() => new ProcessRunner().RunAsync(
            new ProcessRequest("dotnet", [], AppContext.BaseDirectory, timeoutMs), TestContext.Current.CancellationToken));
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
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], ToolRunContext.None, timeout.Token);
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
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], ToolRunContext.None, timeout.Token);
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

    // Regression for a real incident: a tool result containing Cyrillic (or any non-ASCII) text
    // was escaped to `\uXXXX` once when the built-in server built it, then escaped a second time
    // ("\\uXXXX", 7 characters per source character) when the Host serialized the whole result
    // into the string that becomes this tool call's persisted chat message — the same string
    // replayed as context on every later turn. A few genuinely large Russian-language docs pushed
    // one chat over a model's context limit purely on this escaping overhead.
    [Fact]
    public async Task ShouldNotEscapeNonAsciiTextInToolResults()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-non-ascii").FullName;
        try
        {
            // Long enough that fixed JSON scaffolding (field names and the echoed path) is
            // negligible next to the source text, so the assertion below is actually measuring
            // the escaping fix rather than per-call overhead.
            var text = string.Concat(Enumerable.Repeat("Привет, мир! Это тестовый файл с кириллицей.\n", 100));
            var file = Path.Combine(root, "sample.txt");
            await File.WriteAllTextAsync(file, text, TestContext.Current.CancellationToken);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], ToolRunContext.None, timeout.Token);
            var tool = session.Tools.Single(item => item.OriginalName == "read_text_file");
            var result = await session.CallAsync(tool, JsonSerializer.Serialize(new { path = file }), null, timeout.Token);

            result.ModelContent.ShouldContain("Привет, мир!");
            result.ModelContent.ShouldNotContain("\\u04");
            // Successful structured content now wins over the duplicate text block. The remaining
            // overhead is the structured result's field names and path; Unicode text stays literal.
            result.ModelContent.Length.ShouldBeLessThan(text.Length * 4);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldDeleteFilesAndDirectoriesWithinGrantOverStdio()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-delete").FullName;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read", "write", "edit", "delete"])], ToolRunContext.None, timeout.Token);
            var token = timeout.Token;

            var directory = Path.Combine(root, "sub");
            var file = Path.Combine(directory, "sample.txt");
            (await Structured(session, "create_directory", new { path = directory }, token))
                .GetProperty("created").GetBoolean().ShouldBeTrue();
            (await Structured(session, "write_file", new { path = file, content = "alpha\n" }, token))
                .GetProperty("created").GetBoolean().ShouldBeTrue();

            // The size is reported for the file that was removed, not for whatever is left behind.
            var deleted = await Structured(session, "delete_file", new { path = file }, token);
            deleted.GetProperty("deleted").GetBoolean().ShouldBeTrue();
            deleted.GetProperty("bytes").GetInt64().ShouldBe(6);
            File.Exists(file).ShouldBeFalse();
            // A second call has nothing left to remove, and says so instead of claiming success.
            (await Structured(session, "delete_file", new { path = file }, token))
                .GetProperty("deleted").GetBoolean().ShouldBeFalse();

            // Each tool owns one kind of path and points at the other one rather than guessing.
            (await Structured(session, "delete_file", new { path = directory }, token))
                .GetProperty("error").GetString()!.ShouldContain("Use delete_directory");
            (await Structured(session, "delete_file", new { path = Path.Combine(root, "absent.txt") }, token))
                .GetProperty("error").GetString().ShouldBe("File does not exist.");
            var plain = Path.Combine(root, "plain.txt");
            File.WriteAllText(plain, "plain");
            (await Structured(session, "delete_directory", new { path = plain }, token))
                .GetProperty("error").GetString()!.ShouldContain("Use delete_file");
            File.Exists(plain).ShouldBeTrue();
            (await Structured(session, "delete_directory", new { path = Path.Combine(root, "absent") }, token))
                .GetProperty("error").GetString().ShouldBe("Directory does not exist.");

            // Without `recursive` a non-empty directory is refused and stays where it is.
            var nested = Path.Combine(directory, "nested");
            (await Structured(session, "create_directory", new { path = nested }, token))
                .GetProperty("created").GetBoolean().ShouldBeTrue();
            File.WriteAllText(Path.Combine(nested, "keep.txt"), "keep");
            (await Structured(session, "delete_directory", new { path = directory }, token))
                .GetProperty("error").GetString()!.ShouldContain("recursive: true");
            Directory.Exists(directory).ShouldBeTrue();
            File.Exists(Path.Combine(nested, "keep.txt")).ShouldBeTrue();

            var recursive = await Structured(session, "delete_directory", new { path = directory, recursive = true }, token);
            recursive.GetProperty("deleted").GetBoolean().ShouldBeTrue();
            recursive.GetProperty("recursive").GetBoolean().ShouldBeTrue();
            Directory.Exists(directory).ShouldBeFalse();

            var empty = Path.Combine(root, "empty");
            (await Structured(session, "create_directory", new { path = empty }, token))
                .GetProperty("created").GetBoolean().ShouldBeTrue();
            (await Structured(session, "delete_directory", new { path = empty }, token))
                .GetProperty("deleted").GetBoolean().ShouldBeTrue();
            Directory.Exists(empty).ShouldBeFalse();

            // The grant boundary applies to deletion exactly as it does to writing.
            var outside = Path.Combine(Path.GetTempPath(), "ai-client-delete-outside.txt");
            File.WriteAllText(outside, "no");
            try
            {
                (await Structured(session, "delete_file", new { path = outside }, token))
                    .GetProperty("error").GetString()!.ShouldContain("No directory grant");
                File.Exists(outside).ShouldBeTrue();
            }
            finally { File.Delete(outside); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldRefuseDeletionUnderAGrantWithoutTheDeleteCapability()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-delete-capability").FullName;
        try
        {
            var file = Path.Combine(root, "sample.txt");
            File.WriteAllText(file, "alpha");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            // Read/write is not delete: an ordinary editing grant must not be able to unlink a file.
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read", "write", "edit"])], ToolRunContext.None, timeout.Token);
            var token = timeout.Token;

            (await Structured(session, "delete_file", new { path = file }, token))
                .GetProperty("error").GetString()!.ShouldContain("No directory grant");
            (await Structured(session, "delete_directory", new { path = root, recursive = true }, token))
                .GetProperty("error").GetString()!.ShouldContain("No directory grant");
            File.Exists(file).ShouldBeTrue();
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
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read", "write", "edit", "delete"])], ToolRunContext.None, timeout.Token);
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

    [Fact]
    public async Task ShouldGrepFileContentsOverStdio()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-grep").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "one\ntwo needle here\nthree\nfour\nneedle again\n");
            File.WriteAllText(Path.Combine(root, "b.md"), "Needle in another file\n");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], ToolRunContext.None, timeout.Token);
            var token = timeout.Token;

            var plain = await Structured(session, "grep_files", new { path = root, query = "needle" }, token);
            plain.GetProperty("totalMatches").GetInt32().ShouldBe(2);
            plain.GetProperty("filesScanned").GetInt32().ShouldBe(2);
            var file = plain.GetProperty("files").EnumerateArray().Single();
            file.GetProperty("path").GetString().ShouldBe(Path.Combine(root, "a.txt"));
            var first = file.GetProperty("matches").EnumerateArray().First();
            first.GetProperty("line").GetInt32().ShouldBe(2);
            first.GetProperty("column").GetInt32().ShouldBe(5);
            first.GetProperty("text").GetString().ShouldBe("two needle here");

            // Case-sensitive by default, so the second file only shows up when asked for.
            (await Structured(session, "grep_files", new { path = root, query = "needle", ignoreCase = true }, token))
                .GetProperty("files").GetArrayLength().ShouldBe(2);

            // A literal query is not a pattern; the same text as a regular expression is.
            (await Structured(session, "grep_files", new { path = root, query = "need.e" }, token))
                .GetProperty("totalMatches").GetInt32().ShouldBe(0);
            (await Structured(session, "grep_files", new { path = root, query = "need.e", isRegex = true }, token))
                .GetProperty("totalMatches").GetInt32().ShouldBe(2);

            var context = await Structured(session,
                "grep_files", new { path = Path.Combine(root, "a.txt"), query = "needle", contextLines = 2 }, token);
            var withContext = context.GetProperty("files").EnumerateArray().Single()
                .GetProperty("matches").EnumerateArray().First();
            withContext.GetProperty("before").EnumerateArray().Select(item => item.GetString()).ShouldBe(["one"]);
            withContext.GetProperty("after").EnumerateArray().Select(item => item.GetString()).ShouldBe(["three", "four"]);

            (await Structured(session, "grep_files", new { path = root, query = "needle", filePattern = "*.md", ignoreCase = true }, token))
                .GetProperty("files").EnumerateArray().Single()
                .GetProperty("path").GetString().ShouldBe(Path.Combine(root, "b.md"));

            // Lookaround is unsupported by the non-backtracking engine and has to surface as a
            // readable error rather than as an exception escaping the tool.
            (await Structured(session, "grep_files", new { path = root, query = "(?=needle)", isRegex = true }, token))
                .GetProperty("error").GetString().ShouldNotBeNullOrEmpty();
            (await Structured(session, "grep_files", new { path = Path.Combine(root, "absent.txt"), query = "needle" }, token))
                .GetProperty("error").GetString().ShouldBe("Path does not exist.");

            var outside = Path.Combine(Path.GetTempPath(), "ai-client-grep-outside");
            (await Structured(session, "grep_files", new { path = outside, query = "needle" }, token))
                .GetProperty("error").GetString()!.ShouldContain("No directory grant");
        }
        finally { Directory.Delete(root, true); }
    }

    // Three things a content search meets on a real repository and must not pass on to the model:
    // a binary file, a line long enough to fill the whole result by itself, and a file matching on
    // far more lines than anyone wants quoted back.
    [Fact]
    public async Task ShouldSkipBinaryFilesAndBoundLongLinesAndBusyFiles()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-grep-limits").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "image.bin"), [0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65, 0x00, 0x01]);
            File.WriteAllText(Path.Combine(root, "long.txt"), new string('x', 5000) + "needle" + new string('y', 5000) + "\n");
            File.WriteAllText(Path.Combine(root, "busy.txt"), string.Concat(Enumerable.Repeat("needle\n", 40)));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read"])], ToolRunContext.None, timeout.Token);
            var token = timeout.Token;

            var result = await Structured(session, "grep_files", new { path = root, query = "needle" }, token);
            result.GetProperty("filesSkipped").GetInt32().ShouldBe(1);
            result.GetProperty("filesScanned").GetInt32().ShouldBe(2);
            var files = result.GetProperty("files").EnumerateArray()
                .ToDictionary(item => Path.GetFileName(item.GetProperty("path").GetString()!));
            files.Keys.ShouldNotContain("image.bin");

            // The long line comes back as a window around the match rather than in full, while the
            // reported column still points into the real line.
            var window = files["long.txt"].GetProperty("matches").EnumerateArray().Single();
            window.GetProperty("column").GetInt32().ShouldBe(5001);
            var text = window.GetProperty("text").GetString()!;
            text.Length.ShouldBeLessThan(500);
            text.ShouldContain("needle");

            // Every matching line is counted even though only a handful are quoted.
            var busy = files["busy.txt"];
            busy.GetProperty("matchCount").GetInt32().ShouldBe(40);
            busy.GetProperty("matches").GetArrayLength().ShouldBe(5);
            busy.GetProperty("truncated").GetBoolean().ShouldBeTrue();

            (await Structured(session, "grep_files", new { path = root, query = "needle", maxMatchesPerFile = 40 }, token))
                .GetProperty("files").EnumerateArray()
                .Single(item => Path.GetFileName(item.GetProperty("path").GetString()!) == "busy.txt")
                .GetProperty("matches").GetArrayLength().ShouldBe(40);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ShouldPackListReadAndUnpackArchiveWithinGrantOverStdio()
    {
        var root = Directory.CreateTempSubdirectory("ai-client-zip").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "src"));
            var nested = Directory.CreateDirectory(Path.Combine(source.FullName, "nested"));
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "note.txt"), "alpha\nbeta\n", TestContext.Current.CancellationToken);
            // A default-excluded directory must not end up in the archive any more than it ends up
            // in a directory listing.
            var skipped = Directory.CreateDirectory(Path.Combine(source.FullName, ".git"));
            await File.WriteAllTextAsync(Path.Combine(skipped.FullName, "HEAD"), "ref: refs/heads/master", TestContext.Current.CancellationToken);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var session = await new DefaultToolSessionFactory(new ToolResultModelProjector()).OpenAsync(
                [new ToolDirectoryGrant(root, true, ["read", "write", "edit", "delete"])], ToolRunContext.None, timeout.Token);
            var token = timeout.Token;

            var archive = Path.Combine(root, "bundle.zip");
            var created = await Structured(session, "zip_create", new { path = archive, paths = (string[])[source.FullName] }, token);
            created.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
            created.GetProperty("entries").GetInt32().ShouldBe(1);
            File.Exists(archive).ShouldBeTrue();

            var listing = await Structured(session, "zip_list", new { path = archive }, token);
            listing.GetProperty("entryCount").GetInt32().ShouldBe(1);
            var entry = listing.GetProperty("entries").EnumerateArray().Single();
            entry.GetProperty("path").GetString().ShouldBe("src/nested/note.txt");
            entry.GetProperty("kind").GetString().ShouldBe("file");

            // The listing can be narrowed by a glob, and a pattern that matches nothing is not an error.
            (await Structured(session, "zip_list", new { path = archive, pattern = "*.md" }, token))
                .GetProperty("entries").GetArrayLength().ShouldBe(0);

            (await Structured(session, "zip_read", new { path = archive, entryPath = "src/nested/note.txt" }, token))
                .GetProperty("content").GetString().ShouldBe("alpha\nbeta\n");
            (await Structured(session, "zip_read", new { path = archive, entryPath = "src/nested/absent.txt" }, token))
                .GetProperty("error").GetString()!.ShouldContain("Use zip_list");

            var destination = Path.Combine(root, "out");
            var extracted = await Structured(session, "zip_extract", new { path = archive, destination }, token);
            extracted.GetProperty("files").GetArrayLength().ShouldBe(1);
            File.ReadAllText(Path.Combine(destination, "src", "nested", "note.txt")).ShouldBe("alpha\nbeta\n");

            // An existing file is refused before anything is written, unless the call asks for it.
            (await Structured(session, "zip_extract", new { path = archive, destination }, token))
                .GetProperty("error").GetString()!.ShouldContain("overwrite");
            (await Structured(session, "zip_extract", new { path = archive, destination, overwrite = true }, token))
                .GetProperty("files").GetArrayLength().ShouldBe(1);
            (await Structured(session, "zip_extract", new { path = archive, destination, pattern = "*.md", overwrite = true }, token))
                .GetProperty("files").GetArrayLength().ShouldBe(0);

            // Zip Slip: an entry whose name climbs out of the destination is refused as a whole,
            // and the file it aimed at is not created.
            var escape = Path.Combine(root, "escape.zip");
            using (var hostile = System.IO.Compression.ZipFile.Open(escape, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(hostile.CreateEntry("../escaped.txt").Open());
                writer.Write("owned");
            }

            (await Structured(session, "zip_extract", new { path = escape, destination = Path.Combine(root, "slip") }, token))
                .GetProperty("error").GetString()!.ShouldContain("outside the destination");
            File.Exists(Path.Combine(root, "escaped.txt")).ShouldBeFalse();

            // A path outside every grant is refused, for reading an archive and for writing one.
            var outside = Path.Combine(Path.GetTempPath(), "ai-client-zip-outside.zip");
            (await Structured(session, "zip_list", new { path = outside }, token))
                .GetProperty("error").GetString()!.ShouldContain("No directory grant");
            (await Structured(session, "zip_create", new { path = outside, paths = (string[])[archive] }, token))
                .GetProperty("error").GetString()!.ShouldContain("No directory grant");
            File.Exists(outside).ShouldBeFalse();

            // A file that is not an archive is reported as such rather than as a crash.
            var plain = Path.Combine(root, "plain.txt");
            await File.WriteAllTextAsync(plain, "not a zip", TestContext.Current.CancellationToken);
            (await Structured(session, "zip_list", new { path = plain }, token))
                .GetProperty("error").GetString()!.ShouldContain("Not a valid zip archive");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<JsonElement> Structured(IToolSession session, string name, object arguments, CancellationToken token)
    {
        var tool = session.Tools.Single(item => item.OriginalName == name);
        var result = await session.CallAsync(tool, JsonSerializer.Serialize(arguments), null, token);
        return result.StructuredContent ?? throw new InvalidOperationException($"Tool '{name}' returned no structured content.");
    }

    private sealed class Grants(params DirectoryGrantSpec[] grants) : IGrantSource
    {
        public IReadOnlyList<DirectoryGrantSpec> Load() => grants;
    }
}
