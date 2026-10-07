namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public sealed class ChatTeamRosterCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A lead with two teammates: Ada reported done; Bo asked a question the lead has not answered
    /// and his run then failed.
    /// </summary>
    internal static (ChatDetails Chat, Guid Ada, Guid Bo, Guid Task, Guid Charter) Team()
    {
        var chatId = Guid.NewGuid();
        var lead = new MessageSender(chatId, chatId, "decision");
        var task = new ChatMessageView(Guid.NewGuid(), null, "User", "Add CSV export with API, UI and tests", T0);
        var plan = new ChatMessageView(Guid.NewGuid(), task.Id, "Assistant", "Assembling the team.", T0.AddSeconds(1));
        var charter = new ChatMessageView(Guid.NewGuid(), plan.Id, "User", "# Team charter\nGoal: export orders.",
            T0.AddSeconds(2), Delivery: MessageDelivery.InTurn, Sender: lead);
        var adaBrief = new ChatMessageView(Guid.NewGuid(), charter.Id, "User", "You are Ada · Backend.", T0.AddSeconds(3), Sender: lead);
        var boBrief = new ChatMessageView(Guid.NewGuid(), charter.Id, "User", "You are Bo · Tests.", T0.AddSeconds(4), Sender: lead);
        var adaDone = new ChatMessageView(Guid.NewGuid(), charter.Id, "User", "Endpoint ready\nGET /orders.csv",
            T0.AddSeconds(10), Sender: new MessageSender(chatId, adaBrief.Id, "done"));
        var boQuestion = new ChatMessageView(Guid.NewGuid(), adaDone.Id, "User", "Which delimiter?",
            T0.AddSeconds(11), Sender: new MessageSender(chatId, boBrief.Id, "question"));
        var chat = new ChatDetails(chatId, Guid.NewGuid(), "Team", T0, T0, 1, null,
            [task, plan, charter, adaBrief, boBrief, adaDone, boQuestion],
            [
                new ChatBranchView(chatId, boQuestion.Id, "Team"),
                new ChatBranchView(adaBrief.Id, adaBrief.Id, "Ada · Backend", chatId, adaBrief.Id, Member: new TeamMember("Ada", "Backend", "teal")),
                new ChatBranchView(boBrief.Id, boBrief.Id, "Bo · Tests", chatId, boBrief.Id, Member: new TeamMember("Bo", "Tests", "amber"))
            ]);
        return (chat, adaBrief.Id, boBrief.Id, task.Id, charter.Id);
    }

    internal static ChatRunSnapshot Run(ChatDetails chat, Guid branch, ChatRunStatus status) =>
        new(chat.ProjectId, chat.Id, branch, status, "", [], false, null, 1);

    [Fact]
    public void TheWholeTeamShouldBeShownFromATeammatesBranch()
    {
        var (chat, ada, bo, task, charter) = Team();

        var roster = new ChatTeamRosterCalculator().Calculate(chat,
            [Run(chat, ada, ChatRunStatus.Completed), Run(chat, bo, ChatRunStatus.Failed), Run(chat, chat.Id, ChatRunStatus.Generating)], ada);

        roster.Task!.MessageId.ShouldBe(task);
        roster.Task.Text.ShouldBe("Add CSV export with API, UI and tests");
        roster.Charter!.MessageId.ShouldBe(charter);
        roster.Members.Select(member => member.Name).ShouldBe(["Lead", "Ada", "Bo"]);
        roster.Members[0].State.ShouldBe(ChatTeamMemberState.Working);

        var adaRow = roster.Members[1];
        adaRow.IsCurrent.ShouldBeTrue();
        adaRow.State.ShouldBe(ChatTeamMemberState.Done);
        adaRow.LastReport!.Text.ShouldBe("Endpoint ready");
        adaRow.Open.ShouldBeNull();

        var boRow = roster.Members[2];
        boRow.State.ShouldBe(ChatTeamMemberState.Stopped);
        boRow.Open!.Intent.ShouldBe("question");
        roster.Done.ShouldBe(1);
        roster.Open.ShouldBe(1);
    }

    [Fact]
    public void AnAnswerFromTheLeadShouldCloseTheQuestion()
    {
        var (chat, _, bo, _, _) = Team();
        var answer = new ChatMessageView(Guid.NewGuid(), bo, "User", "Use a semicolon.", T0.AddSeconds(12),
            Sender: new MessageSender(chat.Id, chat.Id, "answer"));
        chat = chat with
        {
            Messages = [.. chat.Messages, answer],
            Branches = [.. chat.Branches!.Select(branch => branch.Id == bo ? branch with { HeadMessageId = answer.Id } : branch)]
        };

        var roster = new ChatTeamRosterCalculator().Calculate(chat, [], chat.Id);

        roster.Members.Single(member => member.Name == "Bo").Open.ShouldBeNull();
        roster.Members[0].IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void AChatWithoutTeammatesHasNoRoster()
    {
        var chatId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, Guid.NewGuid(), "Plain", T0, T0, 1, null, [], [new ChatBranchView(chatId, null, "Plain")]);

        new ChatTeamRosterCalculator().Calculate(chat, [], null).IsTeam.ShouldBeFalse();
    }
}
