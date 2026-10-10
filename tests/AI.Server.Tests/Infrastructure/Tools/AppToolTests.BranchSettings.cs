namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Tools;
using AI.Contracts.Chats;
using Shouldly;
using System.Text.Json;
using Xunit;

/// <summary>Branch settings through app_chats, app_security and app_read: override, inherit, inherit again.</summary>
public sealed partial class AppToolTests
{
    [Fact]
    public async Task BranchSettingsShouldOverrideForTheBranchAndItsChildrenAndInheritAgain()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var (branch, child) = await BranchesAsync(fixture);
        var connection = (await fixture.GlobalSettings.GetAsync(CancellationToken.None)).Connections[0].Id;
        await using var session = await fixture.OpenAsync();

        var set = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "SetBranchSettings", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            chatId = fixture.ChatId, branchId = branch,
            branchSettings = new { connection = connection.ToString(), approvalMode = "FullAccess" }
        });
        set.GetProperty("applied").GetBoolean().ShouldBeTrue();

        var inherited = await EffectiveAsync(session, fixture, child);
        inherited.GetProperty("connectionId").GetGuid().ShouldBe(connection);
        inherited.GetProperty("approvalMode").GetString().ShouldBe("FullAccess");
        inherited.GetProperty("approvalModeFrom").GetString().ShouldBe("branch \"Branch\"");
        (await EffectiveAsync(session, fixture, branch)).GetProperty("approvalModeFrom").GetString().ShouldBe("this branch");
        (await EffectiveAsync(session, fixture, fixture.ChatId)).GetProperty("approvalMode").GetString().ShouldBe("Ask");

        // Only the named field goes back to the parent; the connection override stays.
        await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "SetBranchSettings", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            chatId = fixture.ChatId, branchId = branch, branchSettings = new { approvalMode = "inherit" }
        });
        var settings = (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!
            .Branches!.Single(item => item.Id == branch).Settings.ShouldNotBeNull();
        settings.ApprovalMode.ShouldBeNull();
        settings.ConnectionId.ShouldBe(connection);

        await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "SetBranchSettings", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            chatId = fixture.ChatId, branchId = branch, branchSettings = new { inheritAll = true }
        });
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!
            .Branches!.Single(item => item.Id == branch).Settings.ShouldBeNull();
        (await EffectiveAsync(session, fixture, child)).GetProperty("connectionFrom").GetString().ShouldBe("chat");
    }

    [Fact]
    public async Task BranchSettingsShouldRefuseInheritanceOnTheMainBranch()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var refused = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "SetBranchSettings", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            branchSettings = new { approvalMode = "inherit" }
        }, expectError: true);
        refused.GetProperty("error").GetString()!.ShouldContain("no parent");

        // The main branch's settings are the chat's own.
        await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "SetBranchSettings", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            branchSettings = new { approvalMode = "Auto" }
        });
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!
            .ApprovalMode.ShouldBe(ToolApprovalMode.Auto);
    }

    [Fact]
    public async Task BranchToolPolicyShouldOverrideOneToolAndInheritAgainWhenRemoved()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var (branch, _) = await BranchesAsync(fixture);
        await using var session = await fixture.OpenAsync();
        var serverId = Guid.NewGuid();

        await AppFixture.CallAsync(session, "app_security", new
        {
            operation = "SetBranchToolPolicy", operationId = Guid.NewGuid(), projectId = fixture.ProjectId,
            chatId = fixture.ChatId, branchId = branch,
            toolPolicy = new { serverId, name = "fs_write", schemaHash = "h", decision = "Deny", maxCallsPerRun = 8, timeoutSeconds = 120 }
        });
        var policies = (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!
            .Branches!.Single(item => item.Id == branch).Settings!.ToolPolicies!;
        policies.ShouldHaveSingleItem().Decision.ShouldBe("Deny");
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.ToolPolicies!.ShouldBeEmpty();

        await AppFixture.CallAsync(session, "app_security", new
        {
            operation = "RemoveBranchToolPolicy", operationId = Guid.NewGuid(), projectId = fixture.ProjectId,
            chatId = fixture.ChatId, branchId = branch, serverId, name = "fs_write", schemaHash = "h"
        });
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!
            .Branches!.Single(item => item.Id == branch).Settings.ShouldBeNull();
    }

    /// <summary>A branch off the main one and a branch below it.</summary>
    private static async Task<(Guid Branch, Guid Child)> BranchesAsync(AppFixture fixture)
    {
        var chat = (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!;
        var root = Guid.NewGuid();
        chat = (await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new AppendChatMessageRequest(root, null, "User", "Start", chat.Revision), CancellationToken.None))!;
        var branch = Guid.NewGuid();
        chat = (await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new AppendChatMessageRequest(branch, root, "User", "Try another way", chat.Revision, BranchId: branch,
                ParentBranchId: fixture.ChatId, BranchTitle: "Branch"), CancellationToken.None))!;
        var child = Guid.NewGuid();
        await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new AppendChatMessageRequest(child, branch, "User", "And deeper", chat.Revision, BranchId: child,
                ParentBranchId: branch, BranchTitle: "Child"), CancellationToken.None);
        return (branch, child);
    }

    private static async Task<JsonElement> EffectiveAsync(IToolSession session, AppFixture fixture, Guid branchId) =>
        (await AppFixture.CallAsync(session, "app_read", new { resource = "Chat", chatId = fixture.ChatId, branchId }))
            .GetProperty("items")[0].GetProperty("effectiveBranchSettings");
}
