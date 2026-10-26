namespace AI.Domain.Tests.Projects;

using Common;
using AI.Domain.Projects;
using Shouldly;
using Xunit;

public sealed class ProjectTests
{
    private readonly DateTimeOffset _createdAt = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
    private readonly ProjectId _projectId =
        new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
    private readonly McpServerId _serverId =
        new(Guid.Parse("019f0000-0000-7000-8000-000000000002"));

    [Fact]
    public void ConstructorThrowsDomainExceptionWhenNameIsEmpty()
    {
        // ReSharper disable once ConvertToLocalFunction
        var action = () => new Project(_projectId, " ", string.Empty, _createdAt);

        var exception = Should.Throw<DomainException>(action);
        exception.Message.ShouldBe("Project name cannot be empty.");
    }

    [Fact]
    public void UpdateDetailsUpdatesDetailsAndTimestampWhenValid()
    {
        var project = CreateProject();
        var updatedAt = _createdAt.AddMinutes(1);

        project.UpdateDetails("  Renamed  ", "  Description  ", updatedAt);

        project.Name.ShouldBe("Renamed");
        project.Description.ShouldBe("Description");
        project.UpdatedAt.ShouldBe(updatedAt);
    }

    [Fact]
    public void AddDirectoryGrantThrowsDomainExceptionWhenIdAlreadyExists()
    {
        var project = CreateProject();
        var grantId = new DirectoryGrantId(Guid.Parse("019f0000-0000-7000-8000-000000000010"));
        var first = new DirectoryGrant(grantId, "Source", @"C:\Project\src", true, ["read"]);
        var duplicate = new DirectoryGrant(grantId, "Docs", @"C:\Project\docs", true, ["read"]);
        project.AddDirectoryGrant(first, _createdAt);

        // ReSharper disable once ConvertToLocalFunction
        var action = () => project.AddDirectoryGrant(duplicate, _createdAt);

        Should.Throw<DomainException>(action);
        project.DirectoryGrants.ShouldHaveSingleItem().ShouldBeSameAs(first);
    }

    [Fact]
    public void RemoveDirectoryGrantRemovesOnlyThatGrant()
    {
        var project = CreateProject();
        var removed = new DirectoryGrant(new DirectoryGrantId(Guid.Parse("019f0000-0000-7000-8000-000000000011")),
            "Source", @"C:\Project\src", true, ["read"]);
        var kept = new DirectoryGrant(new DirectoryGrantId(Guid.Parse("019f0000-0000-7000-8000-000000000012")),
            "Docs", @"C:\Project\docs", true, ["read"]);
        project.AddDirectoryGrant(removed, _createdAt);
        project.AddDirectoryGrant(kept, _createdAt);

        project.RemoveDirectoryGrant(removed.Id, _createdAt.AddMinutes(1));

        project.DirectoryGrants.ShouldHaveSingleItem().ShouldBeSameAs(kept);
        Should.Throw<DomainException>(() => project.RemoveDirectoryGrant(removed.Id, _createdAt.AddMinutes(1)));
    }

    [Fact]
    public void AddingDirectoryGrantsRejectsTheWholeBatchWhenALaterIdConflicts()
    {
        var project = CreateProject();
        var existing = new DirectoryGrant(new DirectoryGrantId(Guid.NewGuid()), "existing", "/existing", true, ["read"]);
        project.AddDirectoryGrant(existing, _createdAt);
        var added = new DirectoryGrant(new DirectoryGrantId(Guid.NewGuid()), "new", "/new", true, ["read"]);
        var conflicting = new DirectoryGrant(existing.Id, "changed", "/other", true, ["write"]);

        Should.Throw<DomainException>(() => project.AddDirectoryGrants([added, conflicting], _createdAt.AddMinutes(1)));

        project.DirectoryGrants.ShouldHaveSingleItem().ShouldBeSameAs(existing);
        project.UpdatedAt.ShouldBe(_createdAt);
    }

    [Fact]
    public void AddingDirectoryGrantsSkipsEquivalentAccessWithoutReplacingNamesOrMergingRights()
    {
        var project = CreateProject();
        var existing = new DirectoryGrant(new DirectoryGrantId(Guid.NewGuid()), "original", "/config", true, ["read"]);
        project.AddDirectoryGrant(existing, _createdAt);
        var equivalent = new DirectoryGrant(new DirectoryGrantId(Guid.NewGuid()), "renamed", "/config", true, ["read"]);
        var writable = new DirectoryGrant(new DirectoryGrantId(Guid.NewGuid()), "writable", "/config", true, ["read", "write"]);

        project.AddDirectoryGrants([existing, equivalent, writable, writable], _createdAt.AddMinutes(1));

        project.DirectoryGrants.Count.ShouldBe(2);
        project.DirectoryGrants.ShouldContain(existing);
        project.DirectoryGrants.ShouldContain(writable);
        existing.DisplayName.ShouldBe("original");
        existing.ToolNames.ShouldBe(["read"]);
    }

    [Fact]
    public void DuplicateIdsCannotChangeAccessEvenWhenTheFirstEntryWasAlreadyCovered()
    {
        var project = CreateProject();
        project.AddDirectoryGrant(new DirectoryGrant(new DirectoryGrantId(Guid.NewGuid()), "existing", "/config", true, ["read"]), _createdAt);
        var id = new DirectoryGrantId(Guid.NewGuid());
        var duplicate = new DirectoryGrant(id, "duplicate", "/config", true, ["read"]);
        var changed = new DirectoryGrant(id, "changed", "/config", true, ["read", "write"]);

        Should.Throw<DomainException>(() => project.AddDirectoryGrants([duplicate, changed], _createdAt.AddMinutes(1)));
        project.DirectoryGrants.ShouldHaveSingleItem().ToolNames.ShouldBe(["read"]);
    }

    [Fact]
    public void SetToolPolicyThrowsDomainExceptionWhenServerIsNotConnected()
    {
        var project = CreateProject();
        var policy = new ToolPolicy(
            new ToolIdentity(_serverId, "read", "first-schema"),
            ToolPolicyDecision.Ask);

        // ReSharper disable once ConvertToLocalFunction
        var action = () => project.SetToolPolicy(policy, _createdAt);

        Should.Throw<DomainException>(action);
        project.ToolPolicies.ShouldBeEmpty();
    }

    [Fact]
    public void SetToolPolicyReplacesStalePolicyWhenSchemaChanges()
    {
        var project = CreateProject();
        project.AddMcpServer(
            new McpServerBinding(_serverId, "Files", McpTransportKind.Stdio, true),
            _createdAt);
        project.SetToolPolicy(
            new ToolPolicy(
                new ToolIdentity(_serverId, "read", "first-schema"),
                ToolPolicyDecision.Allow),
            _createdAt);

        var replacement = new ToolPolicy(
            new ToolIdentity(_serverId, "read", "second-schema"),
            ToolPolicyDecision.Ask);
        project.SetToolPolicy(replacement, _createdAt.AddMinutes(1));

        project.ToolPolicies.ShouldHaveSingleItem().ShouldBeSameAs(replacement);
    }

    [Fact]
    public void UpdateDetailsThrowsDomainExceptionWhenTimestampMovesBackwards()
    {
        var project = CreateProject();

        // ReSharper disable once ConvertToLocalFunction
        var action = () => project.UpdateDetails("Name", string.Empty, _createdAt.AddTicks(-1));

        Should.Throw<DomainException>(action);
    }

    [Fact]
    public void ShouldReferenceAGlobalConnection()
    {
        var project = CreateProject();
        var connectionId = new ConnectionId(Guid.NewGuid());
        project.SetConnection(connectionId, _createdAt);
        project.ConnectionId.ShouldBe(connectionId);
    }

    private Project CreateProject() =>
        new(_projectId, "Project", string.Empty, _createdAt);
}
