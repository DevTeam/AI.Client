namespace AI.Client.Application.Tests.Instructions;

using AI.Client.Application.Chat;
using AI.Client.Application.Instructions;
using AI.Client.Application.Memory;
using AI.Client.Application.Projects;
using AI.Client.Contracts.Instructions;
using AI.Client.Contracts.Memory;
using AI.Client.Contracts.Projects;
using AI.Client.Infrastructure.Projects;
using AI.Client.Infrastructure.Storage;
using AI.Client.Infrastructure.Workspace;
using Moq;
using Shouldly;
using Xunit;

public sealed class StandingInstructionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-client-standing-" + Guid.NewGuid().ToString("N"));
    private readonly string _workspace;
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly JsonMemoryRepository _memoryRepository;
    private readonly JsonProjectInstructionsRepository _instructionsRepository;
    private readonly MemoryService _memory;
    private readonly ProjectInstructionsService _instructions;
    private readonly StandingInstructions _standing;

    public StandingInstructionsTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
        var project = new ProjectDetails(_projectId, "Shop", "An online shop.", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, [new DirectoryGrantSettings(Guid.CreateVersion7(), "Workspace", _workspace, true, ["read"])],
            [], []);
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns(Path.Combine(_root, "data"));
        var files = new PhysicalTextFileSystem();
        _memoryRepository = new JsonMemoryRepository(location.Object, files);
        _instructionsRepository = new JsonProjectInstructionsRepository(location.Object, files);
        _memory = new MemoryService(_memoryRepository, projects.Object, new Uuid7IdGenerator(), new SystemClock());
        _instructions = new ProjectInstructionsService(_instructionsRepository, projects.Object, new SystemClock());
        _standing = new StandingInstructions(projects.Object, _instructionsRepository, new WorkspaceInstructionFileReader(),
            _memory, new ContextTokenEstimator());
    }

    public void Dispose()
    {
        _memoryRepository.Dispose();
        _instructionsRepository.Dispose();
        // The target is generated below the explicitly chosen temporary test root.
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ShouldLayerBaseProjectAndMemoryInPromptOrder()
    {
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_workspace, "AGENTS.md"), "Use tabs.", token);
        (await _instructions.UpdateAsync(_projectId, new UpdateProjectInstructionsRequest("Never touch the payments module.", true, 0),
            token)).Status.ShouldBe(ProjectInstructionsUpdateStatus.Updated);
        await _memory.CreateAsync(new CreateMemoryEntryRequest(MemoryScope.User, null, MemoryKind.Profile, "Name",
            "Nikolay, writes C#."), MemoryAuthor.User, null, token);
        await _memory.CreateAsync(new CreateMemoryEntryRequest(MemoryScope.Project, _projectId, MemoryKind.Fact,
            "Payment provider", "Stripe, test keys only."), MemoryAuthor.Model, Guid.NewGuid(), token);

        var preview = await _standing.BuildAsync(_projectId, true, token);

        preview.Layers.Select(layer => layer.Key).ShouldBe(["app.base", "project.instructions", "memory.index"]);
        preview.Layers[0].Content.ShouldContain("spawn_subtask");
        // Access outside the grants is asked for, not worked around.
        preview.Layers[0].Content.ShouldContain("pathKind 'directories'");
        preview.Layers[0].Content.ShouldContain("SetProjectSecurity");
        preview.Layers[0].Tokens.ShouldBeLessThanOrEqualTo(preview.Layers[0].BudgetTokens);
        var project = preview.Layers[1];
        project.Content.ShouldContain("An online shop.");
        project.Content.ShouldContain("Never touch the payments module.");
        project.Content.ShouldContain("Use tabs.");
        project.Sources.Select(source => source.Name).ShouldContain(Path.Combine(_workspace, "AGENTS.md"));
        var memory = preview.Layers[2].Content;
        // A profile entry is inlined; a plain fact is listed by title only, until the model reads it.
        memory.ShouldContain("Nikolay, writes C#.");
        memory.ShouldContain("Payment provider");
        memory.ShouldNotContain("Stripe, test keys only.");
        preview.TotalTokens.ShouldBe(preview.Layers.Sum(layer => layer.Tokens));
    }

    [Fact]
    public async Task ShouldLeaveInstructionFilesOutWhenSwitchedOffAndCutWhatDoesNotFit()
    {
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_workspace, "CLAUDE.md"), "From the file.", token);
        await _instructions.UpdateAsync(_projectId, new UpdateProjectInstructionsRequest(new string('x', 20_000), false, 0), token);

        var preview = await _standing.BuildAsync(_projectId, false, token);

        var project = preview.Layers.Single(layer => layer.Key == "project.instructions");
        project.Content.ShouldNotContain("From the file.");
        project.Truncated.ShouldBeTrue();
        project.Tokens.ShouldBeLessThanOrEqualTo(project.BudgetTokens);
        // Without the App tools the base prompt does not describe them, and there is nothing to
        // remember with and nothing remembered: no memory layer.
        preview.Layers[0].Content.ShouldNotContain("spawn_subtask");
        preview.Layers[0].Content.ShouldContain("access boundary");
        // The chat draws diagrams whatever tools the run has, so that part is always there.
        preview.Layers[0].Content.ShouldContain("```mermaid");
        preview.Layers[0].Content.ShouldContain("xmlns=\"http://www.w3.org/2000/svg\"");
        preview.Layers.ShouldNotContain(layer => layer.Key == "memory.index");
    }

    [Fact]
    public async Task ShouldRejectStaleRevisionsAndInvalidEntries()
    {
        var token = TestContext.Current.CancellationToken;
        var created = await _memory.CreateAsync(new CreateMemoryEntryRequest(MemoryScope.User, null, MemoryKind.Preference,
            "Style", "Short answers."), MemoryAuthor.User, null, token);
        var entry = created.Entry.ShouldNotBeNull();

        var update = new UpdateMemoryEntryRequest(MemoryKind.Preference, "Style", "Very short answers.", ["tone"], true, true, entry.Revision);
        (await _memory.UpdateAsync(entry.Id, null, update, MemoryAuthor.User, null, token)).Status.ShouldBe(MemoryWriteStatus.Saved);
        (await _memory.UpdateAsync(entry.Id, null, update, MemoryAuthor.User, null, token)).Status.ShouldBe(MemoryWriteStatus.Conflict);
        (await _memory.CreateAsync(new CreateMemoryEntryRequest(MemoryScope.User, null, MemoryKind.Fact, " ", "x"),
            MemoryAuthor.User, null, token)).Status.ShouldBe(MemoryWriteStatus.Rejected);
        (await _memory.SearchAsync("very tone", null, token)).ShouldHaveSingleItem().Id.ShouldBe(entry.Id);
        await Should.ThrowAsync<ArgumentException>(() => _memory.CreateAsync(new CreateMemoryEntryRequest(
            MemoryScope.Project, null, MemoryKind.Fact, "No project", "x"), MemoryAuthor.User, null, token));
    }
}
