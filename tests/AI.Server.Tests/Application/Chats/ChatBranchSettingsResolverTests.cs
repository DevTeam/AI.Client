namespace AI.Application.Tests.Chats;

using AI.Application.Chats;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using Shouldly;
using Xunit;

public sealed class ChatBranchSettingsResolverTests
{
    [Fact]
    public void ChildInheritsNearestOverrideAndResetRestoresParent()
    {
        var chatId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var chatConnection = Guid.NewGuid();
        var parentConnection = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var parentPolicy = new ToolPolicySettings(serverId, "execute", "schema", "Allow", 3, null);
        var childPolicy = new ToolPolicySettings(serverId, "execute", "schema", "Ask", null, 20);
        var branches = new[]
        {
            new ChatBranchView(chatId, null, "Main"),
            new ChatBranchView(parentId, null, "Parent", chatId, Settings: new BranchSettings(
                parentConnection, ToolApprovalMode.Auto, [parentPolicy])),
            new ChatBranchView(childId, null, "Child", parentId, Settings: new BranchSettings(
                ToolPolicies: [childPolicy]))
        };
        var chat = new ChatDetails(chatId, Guid.NewGuid(), "Chat", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 1, chatConnection, [], branches, ApprovalMode: ToolApprovalMode.Ask);
        var resolver = new ChatBranchSettingsResolver();

        resolver.ConnectionId(chat, childId).ShouldBe(parentConnection);
        resolver.ApprovalMode(chat, childId).ShouldBe(ToolApprovalMode.Auto);
        resolver.BranchToolPolicies(chat, childId, serverId, "execute", "schema")
            .ShouldBe([childPolicy, parentPolicy]);

        var reset = chat with { Branches =
            [branches[0], branches[1], branches[2] with { Settings = null }] };
        resolver.ConnectionId(reset, childId).ShouldBe(parentConnection);
        resolver.BranchToolPolicies(reset, childId, serverId, "execute", "schema")
            .ShouldBe([parentPolicy]);
        resolver.ConnectionId(reset, chatId).ShouldBe(chatConnection);
        resolver.ApprovalMode(reset, chatId).ShouldBe(ToolApprovalMode.Ask);
    }
}
