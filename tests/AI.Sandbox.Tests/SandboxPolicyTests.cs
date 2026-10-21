namespace AI.Sandbox.Tests;

using Shouldly;
using Xunit;

public sealed class SandboxPolicyTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    [Fact]
    public void NormalizingMergesSpellingsOfOnePathToTheWidestAccess()
    {
        var policy = new SandboxPolicy([
            new SandboxPath(Root + Path.DirectorySeparatorChar, SandboxAccess.Read),
            new SandboxPath(Path.Combine(Root, "sub", ".."), SandboxAccess.ReadWrite),
            new SandboxPath(Root, SandboxAccess.Read)
        ]);

        policy.Normalize().Paths.ShouldHaveSingleItem().ShouldBe(new SandboxPath(Root, SandboxAccess.ReadWrite));
    }

    [Fact]
    public void NormalizingRefusesARelativePath()
    {
        var policy = new SandboxPolicy([new SandboxPath("relative", SandboxAccess.Read)]);

        Should.Throw<ArgumentException>(() => policy.Normalize());
    }

    [Fact]
    public void ThePolicySurvivesTheLauncherCommandLine()
    {
        var policy = new SandboxPolicy([new SandboxPath(Path.Combine(Root, "a \"quoted\" dir"), SandboxAccess.ReadWrite)], AllowNetwork: false);

        var restored = SandboxPolicy.FromJson(policy.ToJson());

        restored.AllowNetwork.ShouldBeFalse();
        restored.Paths.ShouldBe(policy.Paths);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData(@"a\\b", @"a\\b")]
    public void CommandLineArgumentsAreQuotedTheWayTheCRuntimeSplitsThem(string argument, string expected) =>
        CommandLine.Build("program", [argument]).ShouldBe("program " + expected);

    [Fact]
    public void SeatbeltProfileOpensOnlyThePolicyPathsAndClosesTheNetworkWhenAsked()
    {
        var directory = Directory.CreateTempSubdirectory("ai-sandbox-profile-");
        try
        {
            var profile = SeatbeltProfile.Build(new SandboxPolicy([new SandboxPath(directory.FullName, SandboxAccess.ReadWrite)], AllowNetwork: false));

            profile.ShouldContain("(deny file-read* file-write*)");
            profile.ShouldContain("(allow file-read* file-write* (subpath ");
            profile.ShouldContain(directory.Name);
            profile.ShouldContain("(deny network*)");
        }
        finally
        {
            directory.Delete();
        }
    }
}
