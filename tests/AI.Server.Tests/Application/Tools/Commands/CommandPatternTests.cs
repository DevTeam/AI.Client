namespace AI.Application.Tests.Tools.Commands;

using AI.Application.Tools.Commands;
using Shouldly;
using Xunit;

public sealed class CommandPatternTests
{
    [Theory]
    [InlineData("git status *", new[] { "status" }, true)]
    [InlineData("git status *", new[] { "status", "-s", "--porcelain" }, true)]
    [InlineData("git status *", new[] { "stash" }, false)]
    [InlineData("git status *", new[] { "statusx" }, false)]
    [InlineData("git status", new[] { "status" }, true)]
    [InlineData("git status", new[] { "status", "--porcelain" }, false)]
    [InlineData("git push --force*", new[] { "push", "--force-with-lease" }, true)]
    [InlineData("git push --force *", new[] { "push", "--force", "-f", "origin", "main" }, true)]
    [InlineData("git diff ?*", new[] { "diff" }, false)]
    [InlineData("git *", new[] { "anything", "at", "all" }, true)]
    [InlineData("git *", new string[0], true)]
    [InlineData("dotnet test \"My Tests.csproj\"", new[] { "test", "My Tests.csproj" }, true)]
    [InlineData("dotnet test \"say \"\"hi\"\"\"", new[] { "test", "say \"hi\"" }, true)]
    [InlineData(@"dotnet test C:\src\*.csproj", new[] { "test", @"C:\src\a.csproj" }, true)]
    public void ShouldMatchWordByWord(string text, string[] tokens, bool matches)
    {
        var pattern = CommandPattern.Parse(text, caseSensitive: false);

        pattern.Matches(Command("git" == pattern.Name ? "git" : "dotnet", tokens)).ShouldBe(matches);
    }

    [Fact]
    public void ShouldNameTheProgramTheWayCommandsDo()
    {
        CommandPattern.Parse("GIT.EXE status", caseSensitive: false).Text.ShouldBe("git status");
        CommandPattern.Parse("Git status", caseSensitive: true).Name.ShouldBe("Git");
        CommandPattern.Parse("git   status   * ", caseSensitive: false).Text.ShouldBe("git status *");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("* status")]
    [InlineData("gi? status")]
    [InlineData("git \"status")]
    public void ShouldRefuseAPatternThatNamesNoProgram(string text)
    {
        CommandPattern.TryParse(text, false, out var pattern, out var error).ShouldBeFalse();
        pattern.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ShouldRankTheMoreSpecificPatternHigher()
    {
        var all = CommandPattern.Parse("git *", false).Specificity;
        var push = CommandPattern.Parse("git push *", false).Specificity;
        var main = CommandPattern.Parse("git push origin main", false).Specificity;
        var force = CommandPattern.Parse("git push --force*", false).Specificity;

        push.CompareTo(all).ShouldBePositive();
        main.CompareTo(push).ShouldBePositive();
        // Both name push exactly; the mask ends where the command must, the star does not.
        force.CompareTo(push).ShouldBePositive();
        CommandPattern.Parse("git push origin main", false).IsExact.ShouldBeTrue();
        CommandPattern.Parse("git push *", false).IsExact.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReadBackWhatItWrites()
    {
        string[] words = ["dotnet", "test", "My Tests.csproj", "", "say \"hi\"", @"C:\dir\"];

        var text = CommandPattern.Format(words);

        var pattern = CommandPattern.Parse(text, caseSensitive: true);
        pattern.Words.ShouldBe(words[1..]);
    }

    private static ParsedCommand Command(string name, string[] tokens) =>
        new(name, "/bin/" + name, true, [], tokens, "/work", [], false, [], false);
}
