namespace AI.Application.Tests.Runs;

using AI.Application.Runs;
using AI.Contracts.Chats;
using Shouldly;
using Xunit;

public sealed class ModelMessageHeaderTests
{
    private static readonly Guid ChatId = Guid.NewGuid();
    private static readonly Guid Teammate = Guid.NewGuid();

    private static readonly ChatDetails Chat = new(ChatId, Guid.NewGuid(), "Team", DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, 1, null, [],
        [new ChatBranchView(ChatId, null, "Team"), new ChatBranchView(Teammate, null, "Backend — orders API", ChatId)]);

    private static string Apply(MessageSender? sender, MessageDelivery delivery = MessageDelivery.Turn) =>
        new ModelMessageHeader().Apply(new ChatMessageView(Guid.NewGuid(), null, "User", "Text",
            DateTimeOffset.UnixEpoch, Delivery: delivery, Sender: sender), Chat, "Text");

    [Fact]
    public void AnOrdinaryMessageShouldReachTheModelAsWritten() => Apply(null).ShouldBe("Text");

    [Fact]
    public void ATeamMessageFromTheLeadShouldNameTheTeammatesProtocol() =>
        Apply(new MessageSender(ChatId, ChatId, "answer")).ShouldBe(
            $"[From the main branch (branchId {ChatId}) (answer); "
            + "team message from the lead: keep to the team-contribute protocol]\nText");

    [Fact]
    public void ATeamMessageToTheLeadShouldNameTheBranchAndTheLeadsProtocol() =>
        Apply(new MessageSender(ChatId, Teammate, "done"), MessageDelivery.InTurn).ShouldBe(
            $"[From branch \"Backend — orders API\" (branchId {Teammate}) (done); "
            + "team message to the lead: handle it with the team-coordinate protocol; "
            + "added while you were working: take it into account and continue the task]\nText");

    [Fact]
    public void ATeammateShouldBeNamedByItsIdentityAndTheMainBranchAsTheLead()
    {
        var member = Guid.NewGuid();
        var team = Chat with
        {
            Branches = [new ChatBranchView(ChatId, null, "Team"),
                new ChatBranchView(member, null, "renamed by hand", ChatId, Member: new TeamMember("Ada", "Backend", "teal"))]
        };
        string Header(Guid branch) => new ModelMessageHeader().Apply(new ChatMessageView(Guid.NewGuid(), null, "User", "Text",
            DateTimeOffset.UnixEpoch, Sender: new MessageSender(ChatId, branch, "done")), team, "Text");

        Header(member).ShouldStartWith($"[From Ada · Backend (branchId {member}) (done);");
        Header(ChatId).ShouldStartWith($"[From the lead (branchId {ChatId}) (done);");
    }

    [Fact]
    public void AMessageWithoutAnIntentShouldNotBeTreatedAsTeamWork()
    {
        // A fork or a message put into another chat names its sender, but is not team work.
        Apply(new MessageSender(ChatId, ChatId)).ShouldNotContain("team");
        Apply(new MessageSender(Guid.NewGuid(), Guid.NewGuid(), "question")).ShouldNotContain("protocol");
    }
}
