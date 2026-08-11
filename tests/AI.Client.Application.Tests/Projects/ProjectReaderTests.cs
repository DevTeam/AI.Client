using AI.Client.Application.Projects;
using AI.Client.Domain.Projects;
using Moq;
using Shouldly;
using Xunit;

namespace AI.Client.Application.Tests.Projects;

public sealed class ProjectReaderTests
{
    [Fact]
    public async Task GetAsyncReturnsProjectViewWhenProjectExists()
    {
        var projectId = new ProjectId(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
        var createdAt = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
        var project = new Project(projectId, "Project", "Description", createdAt);
        var repository = new Mock<IProjectRepository>(MockBehavior.Strict);
        repository
            .Setup(i => i.GetAsync(projectId, CancellationToken.None))
            .ReturnsAsync(project);
        var sut = new ProjectReader(repository.Object);

        var result = await sut.GetAsync(projectId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(projectId.Value);
        result.Name.ShouldBe("Project");
        result.Description.ShouldBe("Description");
        result.CreatedAt.ShouldBe(createdAt);
        result.UpdatedAt.ShouldBe(createdAt);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetAsyncReturnsNullWhenProjectDoesNotExist()
    {
        var projectId = new ProjectId(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
        var repository = new Mock<IProjectRepository>(MockBehavior.Strict);
        repository
            .Setup(i => i.GetAsync(projectId, CancellationToken.None))
            .ReturnsAsync((Project?)null);
        var sut = new ProjectReader(repository.Object);

        var result = await sut.GetAsync(projectId, CancellationToken.None);

        result.ShouldBeNull();
        repository.VerifyAll();
    }
}
