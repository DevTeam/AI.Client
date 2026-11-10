namespace AI.Search.Tests;

using Shouldly;
using Xunit;

/// <summary>The behavior a caller relies on: what is found, what is skipped and what is reported.</summary>
public sealed class SearchEngineTests
{
    [Fact]
    public async Task ReportsAbsolutePathsRelativeToTheRoot()
    {
        using var corpus = new SearchCorpus()
            .File("a/one.cs", "nothing here")
            .File("a/b/two.cs", "var needle = 1;");

        var result = await SearchCorpus.RunAsync(new SearchRequest { Path = corpus.Root, Pattern = "needle" });

        result.Files.Count.ShouldBe(1);
        result.Files[0].Path.ShouldBe(Path.Combine("a", "b", "two.cs"));
        result.Files[0].MatchCount.ShouldBe(1);
        result.Files[0].Matches[0].Line.ShouldBe(1);
        result.Files[0].Matches[0].Column.ShouldBe(5);
        result.Files[0].Matches[0].Text.ShouldBe("var needle = 1;");
    }

    [Fact]
    public async Task ReportsOneMatchPerMatchingLineAndCountsThemPerFile()
    {
        using var corpus = new SearchCorpus().File("many.txt", "match\nmatch match\nother\nmatch\n");

        var result = await SearchCorpus.RunAsync(corpus.Request("match"));

        result.Files[0].MatchCount.ShouldBe(3);
        result.Files[0].Matches.Count.ShouldBe(3);
        result.Files[0].Matches.Select(match => match.Line).ShouldBe([1, 2, 4]);
        result.Totals.Matches.ShouldBe(3);
    }

    [Theory]
    [InlineData("NEEDLE", 0)]
    [InlineData("needle", 1)]
    public async Task SmartCaseMatchesCaseOnlyWhenThePatternHasUppercase(string pattern, int expected)
    {
        using var corpus = new SearchCorpus().File("file.txt", "Needle\n");

        var smart = await SearchCorpus.RunAsync(corpus.Request(pattern));
        var sensitive = await SearchCorpus.RunAsync(corpus.Request(pattern) with { Case = CaseMode.Sensitive });
        var insensitive = await SearchCorpus.RunAsync(corpus.Request(pattern) with { Case = CaseMode.Insensitive });

        smart.Totals.Matches.ShouldBe(expected);
        sensitive.Totals.Matches.ShouldBe(0);
        insensitive.Totals.Matches.ShouldBe(1);
    }

    [Fact]
    public async Task RegexPatternsReportTheirColumnInCharacters()
    {
        using var corpus = new SearchCorpus().File("file.txt", "αβγ public sealed class Named_3\n");

        var result = await SearchCorpus.RunAsync(corpus.Request(@"public\s+sealed\s+class\s+Named_\d")
            with { Kind = PatternKind.Regex });

        result.Files[0].Matches[0].Column.ShouldBe(5);
        result.Files[0].Matches[0].Text.ShouldBe("αβγ public sealed class Named_3");
    }

    [Fact]
    public async Task WholeWordSkipsMatchesInsideLongerWords()
    {
        using var corpus = new SearchCorpus().File("file.txt", "needles\nneedle\nneedle_type\n");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle") with { WholeWord = true });

        result.Files[0].Matches.Select(match => match.Line).ShouldBe([2]);
    }

    [Fact]
    public async Task IncludeAndExcludeGlobsSelectWhatIsSearched()
    {
        using var corpus = new SearchCorpus()
            .File("src/one.cs", "needle")
            .File("src/two.razor", "needle")
            .File("src/vendor/three.cs", "needle")
            .File("docs/four.cs", "needle");

        var included = await SearchCorpus.RunAsync(corpus.Request("needle")
            with { Include = ["*.cs"], Exclude = ["vendor"] });

        included.Files.Select(file => file.Path).ShouldBe([Path.Combine("docs", "four.cs"), Path.Combine("src", "one.cs")]);
    }

    [Fact]
    public async Task ExcludeGlobSkipsAWholeDirectoryAtAnyDepth()
    {
        using var corpus = new SearchCorpus()
            .File("a/node_modules/one.js", "needle")
            .File("b/node_modules/two.js", "needle")
            .File("c/three.js", "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle") with { Exclude = ["node_modules"] });

        result.Files.Select(file => file.Path).ShouldBe([Path.Combine("c", "three.js")]);
    }

    [Fact]
    public async Task GitignoreDecidesWhatIsSearchedAndCanBeTurnedOff()
    {
        using var corpus = new SearchCorpus()
            .File(".git/HEAD", "ref: refs/heads/main")
            .File(".gitignore", "generated/\n*.log\n")
            .File("generated/skipped.cs", "needle")
            .File("build.log", "needle")
            .File("source.cs", "needle");

        var ignored = await SearchCorpus.RunAsync(corpus.Request("needle"));
        var everything = await SearchCorpus.RunAsync(corpus.Request("needle")
            with { RespectIgnoreFiles = false, IncludeHidden = true });

        ignored.Files.Select(file => file.Path).ShouldBe(["source.cs"]);
        everything.Files.Select(file => file.Path).ShouldContain(Path.Combine("generated", "skipped.cs"));
    }

    [Fact]
    public async Task NestedIgnoreFileAppliesBelowItsOwnDirectoryOnly()
    {
        using var corpus = new SearchCorpus()
            .File("top.cs", "needle")
            .File("inner/.ignore", "*.cs\n")
            .File("inner/skipped.cs", "needle")
            .File("inner/kept.txt", "needle")
            .File("inner/deeper/also-skipped.cs", "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));

        result.Files.Select(file => file.Path).ShouldBe([Path.Combine("inner", "kept.txt"), "top.cs"]);
    }

    [Fact]
    public async Task ANestedIgnoreFileIgnoresOnlyItsOwnDirectories()
    {
        using var corpus = new SearchCorpus()
            .File(".git/HEAD", "ref: refs/heads/main")
            .File(".gitignore", "/out/\n")
            .File("out/top.cs", "needle")
            .File("src/out/nested.cs", "needle")
            .File("src/code.cs", "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));

        // The leading slash ties the rule to the directory of its own ignore file.
        result.Files.Select(file => file.Path).ShouldBe([Path.Combine("src", "out", "nested.cs"), Path.Combine("src", "code.cs")], ignoreOrder: true);
    }

    [Fact]
    public async Task RepositoryExcludesAreReadBeforeTheGitignoreFiles()
    {
        using var corpus = new SearchCorpus()
            .File(".git/HEAD", "ref: refs/heads/main")
            .File(".git/info/exclude", "worktrees/\n*.log\n")
            .File(".gitignore", "!kept.log\n")
            .File("worktrees/copy/code.cs", "needle")
            .File("trace.log", "needle")
            .File("kept.log", "needle")
            .File("code.cs", "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));

        result.Files.Select(file => file.Path).ShouldBe(["kept.log", "code.cs"], ignoreOrder: true);
    }

    [Fact]
    public async Task NegatedIgnoreRuleReincludesAFile()
    {
        using var corpus = new SearchCorpus()
            .File(".gitignore", "*.cs\n!keep.cs\n")
            .File("skipped.cs", "needle")
            .File("keep.cs", "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));

        result.Files.Select(file => file.Path).ShouldBe(["keep.cs"]);
    }

    [Fact]
    public async Task HiddenEntriesAreSkippedUnlessAskedFor()
    {
        using var corpus = new SearchCorpus()
            .File(".hidden/notes.cs", "needle")
            .File(".dotfile", "needle")
            .File("visible.cs", "needle");

        var skipped = await SearchCorpus.RunAsync(corpus.Request("needle"));
        var included = await SearchCorpus.RunAsync(corpus.Request("needle") with { IncludeHidden = true });

        skipped.Files.Select(file => file.Path).ShouldBe(["visible.cs"]);
        included.Files.Count.ShouldBe(3);
    }

    [Fact]
    public async Task BinaryFilesAreSkippedAndCounted()
    {
        using var corpus = new SearchCorpus()
            .Bytes("data.bin", [0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65, 0x00, 0x01])
            .File("text.txt", "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));
        var kept = await SearchCorpus.RunAsync(corpus.Request("needle") with { SkipBinary = false });

        result.Files.Select(file => file.Path).ShouldBe(["text.txt"]);
        result.Totals.FilesSkipped.ShouldBe(1);
        kept.Totals.FilesSkipped.ShouldBe(0);
    }

    [Fact]
    public async Task FilesOverTheSizeLimitAreSkipped()
    {
        using var corpus = new SearchCorpus().File("large.txt", new string('x', 4096) + "needle");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle") with { MaxFileBytes = 1024 });

        result.Files.ShouldBeEmpty();
        result.Totals.FilesSkipped.ShouldBe(1);
    }

    [Fact]
    public async Task MatchCapStopsTheSearchAndSaysSo()
    {
        using var corpus = new SearchCorpus();
        for (var index = 0; index < 50; index++)
        {
            corpus.File($"d{index:00}/file.txt", "needle\nneedle\n");
        }

        // One worker, so the cap is checked before every file: a parallel search completes the files
        // already in flight and reports them, which makes the exact total depend on timing.
        var result = await SearchCorpus.RunAsync(corpus.Request("needle") with { MaxMatches = 10, Parallelism = 1 });

        result.Truncated.ShouldBeTrue();
        result.Totals.Matches.ShouldBe(10);
        result.Files.Sum(file => file.Matches.Count).ShouldBe(10);
    }

    [Fact]
    public async Task FileOutputReportsEachFileOnceWithoutLines()
    {
        using var corpus = new SearchCorpus()
            .File("one.txt", "needle\nneedle\n")
            .File("two.txt", "needle\n")
            .File("three.txt", "nothing");

        var result = await SearchCorpus.RunAsync(corpus.Request("needle") with { Output = MatchOutput.Files });

        result.Files.Select(file => file.Path).ShouldBe(["one.txt", "two.txt"]);
        result.Files.ShouldAllBe(file => file.Matches.Count == 0);
        result.Totals.FilesWithMatches.ShouldBe(2);
    }

    [Fact]
    public async Task ASingleFileIsSearchedDirectly()
    {
        using var corpus = new SearchCorpus().File("only.txt", "first\nneedle\n");

        var result = await SearchCorpus.RunAsync(new SearchRequest
        {
            Path = Path.Combine(corpus.Root, "only.txt"),
            Pattern = "needle"
        });

        result.Files.Count.ShouldBe(1);
        result.Files[0].Path.ShouldBe("only.txt");
        result.Files[0].Matches[0].Line.ShouldBe(2);
    }

    [Fact]
    public async Task CrLfLineEndingsDoNotBecomePartOfTheReportedLine()
    {
        using var corpus = new SearchCorpus().Bytes("crlf.txt", "first\r\nneedle\r\n"u8.ToArray());

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));

        result.Files[0].Matches[0].Text.ShouldBe("needle");
        result.Files[0].Matches[0].Line.ShouldBe(2);
    }

    [Fact]
    public async Task ALongLineIsCutToAWindowAroundTheMatch()
    {
        using var corpus = new SearchCorpus().File("min.js", new string('a', 5000) + "needle" + new string('b', 5000));

        var result = await SearchCorpus.RunAsync(corpus.Request("needle"));

        var text = result.Files[0].Matches[0].Text;
        text.Length.ShouldBeLessThan(500);
        text.ShouldContain("needle");
        result.Files[0].Matches[0].Column.ShouldBe(5001);
    }

    [Fact]
    public async Task AnInvalidPathFails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "AI.Search.Tests", Guid.NewGuid().ToString("N"));

        await Should.ThrowAsync<DirectoryNotFoundException>(async () =>
            await SearchCorpus.RunAsync(new SearchRequest { Path = missing, Pattern = "needle" }));
    }

    [Fact]
    public async Task AnInvalidRegexFails()
    {
        using var corpus = new SearchCorpus().File("file.txt", "needle");

        await Should.ThrowAsync<ArgumentException>(async () =>
            await SearchCorpus.RunAsync(corpus.Request("(unclosed") with { Kind = PatternKind.Regex }));
    }

    [Fact]
    public async Task ParallelAndSequentialSearchesReportTheSameFiles()
    {
        using var corpus = new SearchCorpus();
        for (var index = 0; index < 40; index++)
        {
            corpus.File($"d{index % 5}/file{index:00}.txt", index % 3 == 0 ? "needle" : "other");
        }

        var sequential = await SearchCorpus.RunAsync(corpus.Request("needle") with { Parallelism = 1 });
        var parallel = await SearchCorpus.RunAsync(corpus.Request("needle") with { Parallelism = 8 });

        parallel.Files.Select(file => file.Path).ShouldBe(sequential.Files.Select(file => file.Path));
        parallel.Totals.Matches.ShouldBe(sequential.Totals.Matches);
    }
}