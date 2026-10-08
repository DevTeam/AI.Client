namespace AI.Application.Tests.Instructions;

using AI.Application.Chat;
using AI.Application.Instructions;
using AI.Application.Memory;
using AI.Application.Projects;
using AI.Application.Skills;
using AI.Application.Settings;
using AI.Contracts.Settings;
using AI.Contracts.Instructions;
using AI.Contracts.Memory;
using AI.Contracts.Projects;
using AI.Infrastructure.Projects;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Infrastructure.Workspace;
using Moq;
using Shouldly;
using Xunit;

public sealed class StandingInstructionsTests : IDisposable
{
    private readonly string _workspace = Path.Combine("workspace", "shop");
    private readonly Dictionary<string, string> _instructionFiles = new(StringComparer.Ordinal);
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly JsonMemoryRepository _memoryRepository;
    private readonly JsonProjectInstructionsRepository _instructionsRepository;
    private readonly MemoryService _memory;
    private readonly ProjectInstructionsService _instructions;
    private readonly StandingInstructions _standing;

    public StandingInstructionsTests()
    {
        var project = new ProjectDetails(_projectId, "Shop", "An online shop.", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, [new DirectoryGrantSettings(Guid.CreateVersion7(), "Workspace", _workspace, true, ["read"])],
            [], []);
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns(Path.Combine("data", "shop"));
        var files = new MemoryFileSystem();
        _memoryRepository = new JsonMemoryRepository(location.Object, files);
        _instructionsRepository = new JsonProjectInstructionsRepository(location.Object, files);
        _memory = new MemoryService(_memoryRepository, projects.Object, new Uuid7IdGenerator(), new SystemClock());
        _instructions = new ProjectInstructionsService(_instructionsRepository, projects.Object, new SystemClock());
        var settings = new Mock<IGlobalSettingsRepository>();
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GlobalSettings(
            [new ConnectionSettings(Guid.NewGuid(), "Large", "https://example.test/v1", "model", true, true, false,
                ContextWindowTokens: 131_072)], [], []));
        var instructionReader = new Mock<IInstructionFileReader>();
        instructionReader.Setup(item => item.ReadAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> roots, CancellationToken _) =>
                (IReadOnlyList<InstructionFile>)_instructionFiles
                    .Where(file => roots.Contains(Path.GetDirectoryName(file.Key)!))
                    .Select(file => new InstructionFile(file.Key, file.Value, false)).ToArray());
        _standing = new StandingInstructions(projects.Object, _instructionsRepository, instructionReader.Object,
            _memory, new SkillGuide(new BuiltInSkillCatalog()), new ContextTokenEstimator(),
            new AdaptiveContextPolicy(new ContextTokenEstimator(), new ConnectionContextLimitsResolver()), settings.Object, new ConnectionChoice());
    }

    public void Dispose()
    {
        _memoryRepository.Dispose();
        _instructionsRepository.Dispose();
    }

    [Fact]
    public async Task ShouldLayerBaseProjectAndMemoryInPromptOrder()
    {
        var token = TestContext.Current.CancellationToken;
        _instructionFiles[Path.Combine(_workspace, "AGENTS.md")] = "Use tabs.";
        (await _instructions.UpdateAsync(_projectId, new UpdateProjectInstructionsRequest("Never touch the payments module.", true, 0),
            token)).Status.ShouldBe(ProjectInstructionsUpdateStatus.Updated);
        await _memory.CreateAsync(new CreateMemoryEntryRequest(MemoryScope.User, null, MemoryKind.Profile, "Name",
            "Nikolay, writes C#."), MemoryAuthor.User, null, token);
        await _memory.CreateAsync(new CreateMemoryEntryRequest(MemoryScope.Project, _projectId, MemoryKind.Fact,
            "Payment provider", "Stripe, test keys only."), MemoryAuthor.Model, Guid.NewGuid(), token);

        var preview = await _standing.BuildAsync(_projectId, true, token);

        preview.Layers.Select(layer => layer.Key).ShouldBe(["app.base", "project.instructions", "memory.index", "skills.catalog"]);
        preview.Layers[0].Content.ShouldContain("spawn_subtask");
        // Access outside the grants is asked for, not worked around.
        preview.Layers[0].Content.ShouldContain("pathKind 'directories'");
        preview.Layers[0].Content.ShouldContain("AddDirectoryGrant");
        preview.Layers[0].Content.ShouldContain("Specify the language after the opening fence");
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
    public async Task ShouldLeaveInstructionFilesOutWhenSwitchedOffAndPreserveUserRules()
    {
        var token = TestContext.Current.CancellationToken;
        _instructionFiles[Path.Combine(_workspace, "CLAUDE.md")] = "From the file.";
        await _instructions.UpdateAsync(_projectId, new UpdateProjectInstructionsRequest(new string('x', 20_000), false, 0), token);

        var preview = await _standing.BuildAsync(_projectId, false, token);

        var project = preview.Layers.Single(layer => layer.Key == "project.instructions");
        project.Content.ShouldNotContain("From the file.");
        project.Truncated.ShouldBeFalse();
        project.Content.ShouldContain(new string('x', 20_000));
        // Without the App tools the base prompt does not describe them, and there is nothing to
        // remember with and nothing remembered: no memory layer.
        preview.Layers[0].Content.ShouldNotContain("spawn_subtask");
        preview.Layers[0].Content.ShouldContain("access boundary");
        // The chat draws diagrams whatever tools the run has, so that part is always there.
        preview.Layers[0].Content.ShouldContain("```mermaid");
        preview.Layers[0].Content.ShouldContain("xmlns=\"http://www.w3.org/2000/svg\"");
        preview.Layers.ShouldNotContain(layer => layer.Key == "memory.index");
        preview.Layers.ShouldNotContain(layer => layer.Key == "skills.catalog");
    }

    [Fact]
    public async Task ShouldListEverySkillTheModelMayRunWithItsParameters()
    {
        var preview = await _standing.BuildAsync(_projectId, true, TestContext.Current.CancellationToken);

        var skills = preview.Layers.Single(layer => layer.Key == "skills.catalog");
        skills.Truncated.ShouldBeFalse();
        skills.Tokens.ShouldBeLessThanOrEqualTo(skills.BudgetTokens);
        foreach (var skill in new BuiltInSkillCatalog().List().Where(skill => skill.Kind == AI.Contracts.Skills.SkillKinds.Playbook))
            skills.Content.ShouldContain($"\n- {skill.Id}: ");
        // The catalog says when to check it, and how to leave one skill for another.
        skills.Content.ShouldContain("before other tools");
        skills.Content.ShouldContain("mcp_app__skill_search");
        skills.Content.ShouldContain("mcp_app__run_skill");
        skills.Content.ShouldNotContain("app_skill_search");
        skills.Content.ShouldNotContain("app_run_skill");
        skills.Content.ShouldContain("different task");
        skills.Content.ShouldContain("\n- code-feature-implement: Implement a feature");
        skills.Content.ShouldContain("(goal, scope)");
        skills.Content.ShouldContain("\n- chat-rename: ");
        skills.Content.ShouldContain("chat_id*, mode*");
        // An executor the application runs on its own schedule is not the model's to pick.
        skills.Content.ShouldNotContain("chat-reply-suggest");
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
