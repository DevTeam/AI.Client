namespace AI.Application.Tests.Tools.Commands;

using System.Text.Json;
using AI.Application.Tools.Commands;
using AI.Contracts.FileSystem;
using Shouldly;
using Xunit;

public sealed class CommandParserTests
{
    private static readonly IPath Paths = TestPrograms.Paths;
    private static readonly string Root = TestPrograms.Root;
    private static readonly string Project = TestPrograms.Project;
    private static readonly string LocalGit = TestPrograms.LocalGit;
    private static readonly IReadOnlyList<CommandGrant> Grants = TestPrograms.Grants;

    [Theory]
    [InlineData("git", new[] { "status", "--porcelain" }, "git status --porcelain")]
    [InlineData("GIT.EXE", new[] { "status" }, "git status")]
    [InlineData("git", new[] { "-C", ".", "diff", "--stat" }, "git -C . diff --stat")]
    [InlineData("git", new[] { "push", "-f", "origin", "main" }, "git push --force -f origin main")]
    [InlineData("git", new[] { "push", "origin", "+main" }, "git push --force origin +main")]
    [InlineData("git", new[] { "push", "-uf", "origin", "main" }, "git push --force -uf origin main")]
    [InlineData("dotnet", new[] { "test", "My Tests.csproj" }, "dotnet test \"My Tests.csproj\"")]
    public async Task ShouldSpellTheSameCommandOneWay(string executable, string[] arguments, string display)
    {
        // Only Windows reads GIT.EXE as git.
        if (!OperatingSystem.IsWindows() && executable.Any(char.IsUpper)) return;

        var command = await ParseAsync(executable, arguments);

        command.Display.ShouldBe(display);
        command.OnPath.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldTellAnotherFileFromTheInstalledProgram()
    {
        var command = await ParseAsync(LocalGit, ["status"]);

        command.Name.ShouldBe("git");
        command.ExecutablePath.ShouldBe(LocalGit);
        command.OnPath.ShouldBeFalse();
        var check = CommandGuard.Check(command, Grants, Paths);
        check.BlocksAllow.ShouldBeTrue();
        check.Warnings.ShouldContain(warning => warning.Contains(LocalGit, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new[] { "-c", "alias.status=!rm -rf /", "status" }, "-c")]
    [InlineData(new[] { "--config-env=core.pager=X", "log" }, "--config-env")]
    [InlineData(new[] { "--exec-path=/tmp", "status" }, "--exec-path")]
    [InlineData(new[] { "fetch", "--upload-pack=evil", "origin" }, "--upload-pack")]
    [InlineData(new[] { "clone", "-u", "evil", "https://example.test/r.git" }, "-u")]
    public async Task ShouldBlockOptionsThatRunOtherCommands(string[] arguments, string option)
    {
        var check = CommandGuard.Check(await ParseAsync("git", arguments), Grants, Paths);

        check.Command.UnsafeOptions.ShouldContain(option);
        check.BlocksAllow.ShouldBeTrue();
        CommandGuard.Options(check).Count.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task ShouldCheckEveryPathAgainstTheProject()
    {
        var outside = Path.Combine(Root, "secret", "keys");

        (await CheckAsync("git", ["diff", "--output=" + outside])).BlocksAllow.ShouldBeTrue();
        (await CheckAsync("git", ["-C", Path.Combine(Root, "other"), "status"])).BlocksAllow.ShouldBeTrue();
        (await CheckAsync("git", ["add", Path.Combine("..", "..", "secret")])).BlocksAllow.ShouldBeTrue();
        (await CheckAsync("git", ["status"], workingDirectory: Root)).BlocksAllow.ShouldBeTrue();
        (await CheckAsync("git", ["status"], workingDirectory: null)).BlocksAllow.ShouldBeTrue();

        (await CheckAsync("git", ["diff", "origin/main", "--", "src/a.cs"])).BlocksAllow.ShouldBeFalse();
        (await CheckAsync("git", ["-C", "src", "log", "-1"])).BlocksAllow.ShouldBeFalse();
        (await CheckAsync("git", ["clone", "https://example.test/r.git"])).BlocksAllow.ShouldBeFalse();
    }

    [Theory]
    [InlineData(new[] { "push", "--force-with-lease" }, true)]
    [InlineData(new[] { "push", "origin", ":old" }, true)]
    [InlineData(new[] { "reset", "--hard", "HEAD~1" }, true)]
    [InlineData(new[] { "clean", "-fdx" }, true)]
    [InlineData(new[] { "branch", "-D", "topic" }, true)]
    [InlineData(new[] { "checkout", "--", "." }, true)]
    [InlineData(new[] { "restore", "src/a.cs" }, true)]
    [InlineData(new[] { "restore", "--staged", "src/a.cs" }, false)]
    [InlineData(new[] { "stash", "drop" }, true)]
    [InlineData(new[] { "stash", "push" }, false)]
    [InlineData(new[] { "push", "origin", "main" }, false)]
    [InlineData(new[] { "status" }, false)]
    [InlineData(new[] { "commit", "-m", "x" }, false)]
    public async Task ShouldRecognizeCommandsThatDiscardWork(string[] arguments, bool destructive)
    {
        var check = await CheckAsync("git", arguments);

        check.Command.IsDestructive.ShouldBe(destructive);
        check.ExactOnly.ShouldBe(destructive);
    }

    [Fact]
    public async Task ShouldOfferTheSubcommandFirstAndOnlyTheExactCommandForAShell()
    {
        CommandGuard.Options(await CheckAsync("git", ["log", "--oneline", "-5"]))
            .ShouldBe(["git log *", "git log --oneline -5", "git *"]);
        CommandGuard.Options(await CheckAsync("dotnet", ["--info"])).ShouldBe(["dotnet --info", "dotnet *"]);

        var shell = await CheckAsync("pwsh", ["-NoProfile", "-File", "scripts/check.ps1"]);
        shell.Command.IsInterpreter.ShouldBeTrue();
        CommandGuard.Options(shell).ShouldBe(["pwsh -NoProfile -File scripts/check.ps1"]);

        CommandGuard.Options(await CheckAsync("git", ["add", "*.cs"])).ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldReadWindowsSwitchesAsOptions()
    {
        // Elsewhere /nodeReuse:false is a file in the root.
        if (!OperatingSystem.IsWindows()) return;

        var check = await CheckAsync("dotnet", ["build", "src/App.csproj", "--no-restore", "-m:1", "/nodeReuse:false"]);

        check.BlocksAllow.ShouldBeFalse();
        check.Command.Paths.ShouldBe([Paths.GetFullPath("src/App.csproj", Project)]);

        var outside = await CheckAsync("dotnet", ["build", "/p:OutDir=..\\..\\out"]);
        outside.BlocksAllow.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldLeaveCallsThatNameNoProgram()
    {
        var parser = TestPrograms.Parser();

        (await parser.ParseAsync("""{"arguments":["status"]}""", CancellationToken.None)).ShouldBeNull();
        (await parser.ParseAsync("[]", CancellationToken.None)).ShouldBeNull();
        (await parser.ParseAsync("{", CancellationToken.None)).ShouldBeNull();
    }

    private static async Task<CommandCheck> CheckAsync(string executable, string[] arguments, string? workingDirectory = "")
        => CommandGuard.Check(await ParseAsync(executable, arguments, workingDirectory), Grants, Paths);

    private static async Task<ParsedCommand> ParseAsync(string executable, string[] arguments, string? workingDirectory = "")
    {
        var json = JsonSerializer.Serialize(new
        {
            executable,
            arguments,
            workingDirectory = workingDirectory == "" ? Project : workingDirectory
        });
        return (await TestPrograms.Parser().ParseAsync(json, CancellationToken.None)).ShouldNotBeNull();
    }
}
