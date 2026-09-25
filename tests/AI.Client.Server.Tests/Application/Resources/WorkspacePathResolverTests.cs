namespace AI.Client.Application.Tests.Resources;

using AI.Client.Application.Projects;
using AI.Client.Application.Resources;
using AI.Client.Contracts.Projects;
using AI.Client.Contracts.Resources;
using AI.Client.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

public sealed class WorkspacePathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-client-resolve-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // The target is generated below the explicitly chosen temporary test root.
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ShouldResolveWrittenPathsAgainstReadableRootsOnly()
    {
        var token = TestContext.Current.CancellationToken;
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(Path.Combine(first, "docs"));
        Directory.CreateDirectory(Path.Combine(second, "src"));
        Directory.CreateDirectory(outside);
        var program = Path.Combine(second, "src", "Program.cs");
        await File.WriteAllTextAsync(program, "class Program {}", token);
        var secret = Path.Combine(outside, "secret.txt");
        await File.WriteAllTextAsync(secret, "not granted", token);
        var projectId = Guid.CreateVersion7();
        var project = new ProjectDetails(projectId, "Project", "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1,
        [
            new DirectoryGrantSettings(Guid.CreateVersion7(), "First", first, true, ["read"]),
            new DirectoryGrantSettings(Guid.CreateVersion7(), "Second", second, true, ["read", "write", "edit", "delete"])
        ], [], []);
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        var resolver = new WorkspacePathResolver(projects.Object, new PhysicalDirectoryBrowser(), new ProjectPathAccess());

        var result = await resolver.ResolveAsync(projectId,
            ["src/Program.cs:12", "./src", program, "docs", secret, "missing.cs", "../outside/secret.txt"], token);

        result.Select(item => item.Kind).ShouldBe(
            [ChatResourceKind.File, ChatResourceKind.Directory, ChatResourceKind.File, ChatResourceKind.Directory,
                ChatResourceKind.File, null, null]);
        result.Select(item => item.Access).ShouldBe([PathAccess.ReadWrite, PathAccess.ReadWrite, PathAccess.ReadWrite,
            PathAccess.Read, PathAccess.None, PathAccess.None, PathAccess.None]);
        result[0].Input.ShouldBe("src/Program.cs:12");
        result[0].Path.ShouldBe(program, StringCompareShould.IgnoreCase);
        // A relative path is tried under every root in grant order; "docs" only exists under the first.
        result[3].Path.ShouldBe(Path.Combine(first, "docs"), StringCompareShould.IgnoreCase);
        // An absolute path outside every grant is reported as there but unreadable, so access can be
        // granted; a relative one that climbs out of the roots names nothing.
        result[4].Path.ShouldBe(secret, StringCompareShould.IgnoreCase);
        result[6].Path.ShouldBeNull();
    }
}
