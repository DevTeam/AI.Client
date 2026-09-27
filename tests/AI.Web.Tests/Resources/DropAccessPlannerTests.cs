namespace AI.Web.Tests.Resources;

using AI.Contracts.Resources;
using AI.Web.Resources;
using Shouldly;
using Xunit;

public class DropAccessPlannerTests
{
    private static ResolvedPath File(string path) => new(path, path, ChatResourceKind.File);

    private static ResolvedPath Folder(string path) => new(path, path, ChatResourceKind.Directory);

    private static DropAccessPlan Plan(params ResolvedPath[] blocked) => new DropAccessPlanner().Plan(blocked);

    [Fact]
    public void GrantsTheFileDirectory()
    {
        var plan = Plan(File(@"D:\Logs\today\app.log"));

        plan.Folders.ShouldBe([@"D:\Logs\today"]);
        plan.Refused.ShouldBeEmpty();
    }

    [Fact]
    public void GrantsTheDirectoryAllItemsShare()
    {
        var plan = Plan(File(@"D:\Logs\today\app.log"), File(@"d:\logs\old\app.log"), Folder(@"D:\Logs\archive"));

        plan.Folders.ShouldBe([@"D:\Logs"]);
    }

    [Fact]
    public void GrantsADroppedDirectoryItself()
    {
        Plan(Folder(@"C:\Projects\Alpha")).Folders.ShouldBe([@"C:\Projects\Alpha"]);
    }

    [Fact]
    public void GrantsEachDirectoryWhenOnlyTheDriveIsShared()
    {
        var plan = Plan(File(@"C:\Users\me\a.txt"), File(@"C:\Projects\Alpha\b.txt"), File(@"C:\Projects\Alpha\sub\c.txt"),
            File(@"E:\Data\d.txt"));

        plan.Folders.ShouldBe([@"C:\Users\me", @"C:\Projects\Alpha", @"E:\Data"]);
    }

    [Fact]
    public void RefusesItemsAtARoot()
    {
        var plan = Plan(File(@"D:\diagnostics5.log"), Folder(@"E:\"), File("/notes.md"), File(@"D:\Logs\app.log"));

        plan.Refused.Select(item => item.Path).ShouldBe([@"D:\diagnostics5.log", @"E:\", "/notes.md"]);
        plan.Folders.ShouldBe([@"D:\Logs"]);
    }

    [Fact]
    public void KeepsPosixCase()
    {
        Plan(File("/home/me/A/one.txt"), File("/home/me/a/two.txt")).Folders.ShouldBe(["/home/me"]);
    }

    [Fact]
    public void TreatsAUncShareAsARoot()
    {
        Plan(File(@"\\server\share\x.txt")).Refused.Count.ShouldBe(1);
        Plan(File(@"\\server\share\docs\x.txt"), File(@"\\server\share\docs\y\z.txt")).Folders
            .ShouldBe([@"\\server\share\docs"]);
    }
}
