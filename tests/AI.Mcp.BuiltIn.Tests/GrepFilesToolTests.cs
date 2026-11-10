using AI.Contracts.FileSystem;
using AI.Mcp.BuiltIn.Files;
using AI.Mcp.BuiltIn.Grants;
using AI.Search;
using Shouldly;
using Xunit;

namespace AI.Mcp.BuiltIn.Tests;

/// <summary>
/// The tool over a real tree: the engine does the searching, and these checks cover what the tool
/// decides on top of it — the grant, the filters it sends, the caps it applies and the context lines
/// it adds.
/// </summary>
public sealed class GrepFilesToolTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("ai-grep-files-test-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        Write("src/Program.cs", "class Program\n{\n    static void Main() => Console.WriteLine(\"needle\");\n}");
        Write("src/Other.cs", "// no match here\nvar x = 1;\n");
        Write("docs/readme.md", "A needle in the docs.\n");
        Write("bin/skip.cs", "needle in a build directory\n");
        Write(".hidden/secret.cs", "needle in a hidden directory\n");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not a test failure.
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task FindsMatchesRelativeToTheSearchedDirectoryAndSkipsDefaults()
    {
        var result = await Search("needle");

        result.Error.ShouldBeNull();
        result.TotalMatches.ShouldBe(2);
        result.Files.Select(file => file.Path.Replace('\\', '/'))
            .ShouldBe(["docs/readme.md", "src/Program.cs"], ignoreOrder: true);
    }

    [Fact]
    public async Task SkipsBuildDirectoriesAndHiddenEntriesByDefault()
    {
        var paths = (await Search("needle")).Files.Select(file => file.Path.Replace('\\', '/')).ToArray();

        paths.ShouldNotContain(path => path.Contains("bin", StringComparison.Ordinal));
        paths.ShouldNotContain(path => path.Contains(".hidden", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsLineColumnAndText()
    {
        var result = await Search("needle");

        var match = result.Files.Single(file => file.Path.Replace('\\', '/') == "src/Program.cs")
            .Matches.ShouldHaveSingleItem();
        match.Line.ShouldBe(3);
        match.Column.ShouldBe(46);
        match.Text.ShouldContain("needle");
        // Context nobody asked for is left out rather than sent as empty lists.
        match.Before.ShouldBeNull();
        match.After.ShouldBeNull();
    }

    [Fact]
    public async Task AddsContextLinesOnRequest()
    {
        var result = await Search("needle", contextLines: 1);

        var match = result.Files.Single(file => file.Path.Replace('\\', '/') == "src/Program.cs")
            .Matches.ShouldHaveSingleItem();
        match.Before.ShouldNotBeNull().ShouldHaveSingleItem().ShouldContain('{');
        match.After.ShouldNotBeNull().ShouldHaveSingleItem().ShouldContain('}');
    }

    [Fact]
    public async Task ContextIsShorterAtTheEdgeOfAFile()
    {
        Write("edge/one-line.txt", "needle\n");

        var result = await Search("needle", contextLines: 2);

        var match = result.Files.Single(file => file.Path.Replace('\\', '/') == "edge/one-line.txt")
            .Matches.ShouldHaveSingleItem();
        match.Before.ShouldBeNull();
        match.After.ShouldBeNull();
    }

    [Fact]
    public async Task HonoursFilePatternAndExcludePatterns()
    {
        var onlyDocs = await Search("needle", filePattern: "*.md");
        onlyDocs.Files.Select(file => file.Path.Replace('\\', '/')).ShouldBe(["docs/readme.md"]);

        var withoutDocs = await Search("needle", excludePatterns: ["docs"]);
        withoutDocs.Files.Select(file => file.Path.Replace('\\', '/')).ShouldBe(["src/Program.cs"]);
    }

    [Fact]
    public async Task ExcludeDefaultsOffSearchesBuildDirectoriesAndHiddenEntries()
    {
        var paths = (await Search("needle", excludeDefaults: false)).Files
            .Select(file => file.Path.Replace('\\', '/')).ToArray();

        paths.ShouldContain("bin/skip.cs");
        paths.ShouldContain(".hidden/secret.cs");
    }

    [Fact]
    public async Task MatchingIsCaseSensitiveUnlessAsked()
    {
        Write("case/case.txt", "Needle\nneedle\n");

        (await Search("Needle")).TotalMatches.ShouldBe(1);
        (await Search("needle")).TotalMatches.ShouldBe(3);
        (await Search("needle", ignoreCase: true)).TotalMatches.ShouldBe(4);
    }

    [Fact]
    public async Task RegularExpressionsAndLiteralTextAreDifferentLanguages()
    {
        Write("regex/numbers.txt", "value 42 here\n");

        (await Search("4[0-9]")).TotalMatches.ShouldBe(0);
        (await Search("4[0-9]", isRegex: true)).TotalMatches.ShouldBe(1);
    }

    [Fact]
    public async Task CapsReportedLinesButKeepsTheRealCount()
    {
        Write("many/many.txt", string.Join('\n', Enumerable.Repeat("needle", 10)) + "\n");

        var result = await Search("needle", filePattern: "many.txt", maxMatchesPerFile: 3);

        var file = result.Files.ShouldHaveSingleItem();
        file.Matches.Length.ShouldBe(3);
        file.MatchCount.ShouldBe(10);
        file.Truncated.ShouldBeTrue();
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task SearchesASingleFileByName()
    {
        var result = await Call(Path.Combine(_root, "src", "Program.cs"), "needle");

        result.Error.ShouldBeNull();
        result.Files.ShouldHaveSingleItem().Path.ShouldBe("Program.cs");
    }

    [Fact]
    public async Task RefusesAPathOutsideTheGrants()
    {
        var guard = new PathGuard(new FixedGrants(Path.Combine(_root, "docs")));

        var result = await Call(Path.Combine(_root, "src", "Program.cs"), "needle", guard: guard);

        result.Error.ShouldNotBeNull().ShouldContain("No directory grant");
    }

    [Fact]
    public async Task ReportsAMissingPathAsAnError()
    {
        var result = await Call(Path.Combine(_root, "nothing-here"), "needle");

        result.Error.ShouldNotBeNull();
        result.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectsAnEmptyQueryAndAnInvalidExpression()
    {
        (await Search(string.Empty)).Error.ShouldNotBeNull().ShouldContain("cannot be empty");
        (await Search("([a-z", isRegex: true)).Error.ShouldNotBeNull();
    }

    private GrepFilesTool Tool(IPathGuard? guard = null) =>
        new(guard ?? new PathGuard(new FixedGrants(_root)),
            new BuiltInToolReply(),
            new SearchComposition().Engine,
            new SystemFileSystem(),
            new SystemPath());

    private Task<GrepFilesResult> Search(
        string query,
        bool isRegex = false,
        bool ignoreCase = false,
        string? filePattern = null,
        string[]? excludePatterns = null,
        bool excludeDefaults = true,
        int contextLines = 0,
        int maxMatchesPerFile = 5) =>
        Call(_root, query, isRegex, ignoreCase, filePattern, excludePatterns, excludeDefaults, contextLines,
            maxMatchesPerFile);

    private Task<GrepFilesResult> Call(
        string path,
        string query,
        bool isRegex = false,
        bool ignoreCase = false,
        string? filePattern = null,
        string[]? excludePatterns = null,
        bool excludeDefaults = true,
        int contextLines = 0,
        int maxMatchesPerFile = 5,
        IPathGuard? guard = null) =>
        Tool(guard).ExecuteAsync(path, query, isRegex, ignoreCase, filePattern, excludePatterns, excludeDefaults,
            contextLines, maxMatchesPerFile, Token);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary><paramref name="root"/> as the only grant, so no host configuration takes part.</summary>
    private sealed class FixedGrants(string root) : IGrantSource
    {
        public IReadOnlyList<DirectoryGrantSpec> Load() =>
            [new DirectoryGrantSpec(root, true, new HashSet<GrantCapability> { GrantCapability.Read }, "project")];
    }
}
