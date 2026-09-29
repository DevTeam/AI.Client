namespace AI.Infrastructure.Tests.Workspace;

using AI.Application.Projects;
using AI.Application.Resources;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Infrastructure.Workspace;
using Moq;
using Shouldly;
using Xunit;

public sealed class WorkspaceFileSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-client-search-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly WorkspaceFileSearch _search;

    public WorkspaceFileSearchTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "Web", "Pages"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        File.WriteAllText(Path.Combine(_root, "src", "Web", "Pages", "Home.razor"), "");
        File.WriteAllText(Path.Combine(_root, "README.md"), "");
        var project = new ProjectDetails(_projectId, "Project", "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1,
            [new DirectoryGrantSettings(Guid.CreateVersion7(), "App", _root, true, ["read"])], [], []);
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        var clock = new Mock<IClock>();
        clock.SetupGet(item => item.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        _search = new WorkspaceFileSearch(projects.Object, new ProjectPathAccess(), clock.Object);
    }

    [Fact]
    public async Task ShouldListTheTopOfADirectoryWithoutWaitingForTheIndex()
    {
        var result = await _search.SearchAsync(_projectId, "", 20, TestContext.Current.CancellationToken);

        result.Complete.ShouldBeTrue();
        // Directories first; build output is never offered.
        result.Items.Select(item => item.RelativePath).ShouldBe(["src", "README.md"]);
    }

    [Fact]
    public async Task ShouldOpenAPathByTheProjectDirectoryNameAndKeepItInFront()
    {
        var token = TestContext.Current.CancellationToken;

        var named = await _search.SearchAsync(_projectId, "App/src/W", 20, token);
        var plain = await _search.SearchAsync(_projectId, "src/Web/", 20, token);
        var outside = await _search.SearchAsync(_projectId, "../", 20, token);

        named.Items.ShouldHaveSingleItem().RelativePath.ShouldBe("App/src/Web");
        plain.Items.ShouldHaveSingleItem().Kind.ShouldBe(ChatResourceKind.Directory);
        plain.Items[0].RelativePath.ShouldBe("src/Web/Pages");
        outside.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldFindANameOnceTheBackgroundIndexIsWhole()
    {
        var token = TestContext.Current.CancellationToken;
        await _search.Warm(_projectId, token);

        ResourceSearchResult result;
        var waited = 0;
        do
        {
            result = await _search.SearchAsync(_projectId, "hom", 20, token);
            if (!result.Complete) await Task.Delay(20, token);
        } while (!result.Complete && ++waited < 250);

        result.Complete.ShouldBeTrue();
        result.Items.ShouldHaveSingleItem().RelativePath.ShouldBe("src/Web/Pages/Home.razor");
    }

    public void Dispose()
    {
        // The target is generated below the explicitly chosen temporary test root.
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
