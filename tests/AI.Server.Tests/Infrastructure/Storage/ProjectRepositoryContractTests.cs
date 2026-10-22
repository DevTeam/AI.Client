namespace AI.Infrastructure.Tests.Storage;

using AI.Application.Projects;
using AI.Contracts.Projects;
using AI.Domain.Projects;
using AI.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// What every <see cref="IProjectRepository"/> must do (ADR-014). A database store is a new subclass of
/// this suite.
/// </summary>
public abstract class ProjectRepositoryContractTests
{
    private readonly DateTimeOffset _now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    protected abstract IProjectRepository Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAbsentProjectReadsAsAbsence()
    {
        var repository = Create();
        var id = new ProjectId(Guid.CreateVersion7());

        (await repository.GetAsync(id, Token)).ShouldBeNull();
        (await repository.ListAsync(Token)).ShouldBeEmpty();
        (await repository.DeleteAsync(id, 0, Token)).ShouldBe(ProjectDeleteResult.NotFound());
    }

    [Fact]
    public async Task ASavedProjectIsReadBackAndListed()
    {
        var repository = Create();
        var project = Project("Project");

        (await repository.SaveAsync(project, 0, Token)).ShouldBe(ProjectSaveResult.Saved(1));

        var stored = (await repository.GetAsync(project.Id, Token)).ShouldNotBeNull();
        stored.Revision.ShouldBe(1);
        stored.Project.Name.ShouldBe("Project");
        (await repository.ListAsync(Token)).ShouldHaveSingleItem().Project.Id.ShouldBe(project.Id);
    }

    [Fact]
    public async Task ASaveFromAStaleRevisionIsRefusedAndChangesNothing()
    {
        var repository = Create();
        var project = Project("First");
        await repository.SaveAsync(project, 0, Token);
        project.UpdateDetails("Second", "", _now);
        await repository.SaveAsync(project, 1, Token);

        project.UpdateDetails("Third", "", _now);
        (await repository.SaveAsync(project, 1, Token)).ShouldBe(ProjectSaveResult.Conflict(2));

        (await repository.GetAsync(project.Id, Token))!.Project.Name.ShouldBe("Second");
    }

    [Fact]
    public async Task ADeleteNeedsTheCurrentRevision()
    {
        var repository = Create();
        var project = Project("Project");
        await repository.SaveAsync(project, 0, Token);

        (await repository.DeleteAsync(project.Id, 0, Token)).IsDeleted.ShouldBeFalse();
        (await repository.DeleteAsync(project.Id, 1, Token)).IsDeleted.ShouldBeTrue();

        (await repository.GetAsync(project.Id, Token)).ShouldBeNull();
    }

    private Project Project(string name) => new(new ProjectId(Guid.CreateVersion7()), name, "", _now);
}

public sealed class JsonProjectRepositoryContractTests : ProjectRepositoryContractTests
{
    protected override IProjectRepository Create() =>
        new JsonProjectRepository(new MemoryFileSystem(),
            new ProjectStoragePaths(Mock.Of<IUserStorageLocation>(location => location.RootDirectory == "data")),
            new ProjectDocumentSerializer());
}
