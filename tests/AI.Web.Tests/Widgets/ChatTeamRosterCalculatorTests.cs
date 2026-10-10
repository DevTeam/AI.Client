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
    public void NestedTeamUsesItsParentBranchAsLead()
    {
        var (chat, ada, _, _, _) = Team();
        var charter = new ChatMessageView(Guid.NewGuid(), ada, "User", "# Team charter\nInvestigate API behavior.",
            T0.AddSeconds(20), Sender: new MessageSender(chat.Id, ada, "decision"));
        var childBrief = new ChatMessageView(Guid.NewGuid(), charter.Id, "User", "You are Cleo · Research.",
            T0.AddSeconds(21), Sender: new MessageSender(chat.Id, ada, "decision"));
        var report = new ChatMessageView(Guid.NewGuid(), charter.Id, "User", "Investigation complete.",
            T0.AddSeconds(22), Sender: new MessageSender(chat.Id, childBrief.Id, "done"));
        chat = chat with
        {
            Messages = [.. chat.Messages, charter, childBrief, report],
            Branches = [.. chat.Branches!.Select(branch => branch.Id == ada
                ? branch with { HeadMessageId = report.Id } : branch),
                new ChatBranchView(childBrief.Id, childBrief.Id, "Cleo · Research", ada, childBrief.Id,
                    Member: new TeamMember("Cleo", "Research", "violet"))]
        };

        var roster = new ChatTeamRosterCalculator().Calculate(chat, [], childBrief.Id);

        roster.Members.Select(member => member.BranchId).ShouldBe([ada, childBrief.Id]);
        roster.Members[0].IsLead.ShouldBeTrue();
        roster.Members[1].IsCurrent.ShouldBeTrue();
        roster.Members[1].LastReport!.Text.ShouldBe("Investigation complete.");
        roster.Charter!.MessageId.ShouldBe(charter.Id);
        // The nested lead keeps its identity, its task is the brief it was given, and the team above is named.
        roster.Members[0].Name.ShouldBe("Ada");
        roster.Members[0].Identity.ShouldNotBeNull();
        roster.Task!.Text.ShouldBe("You are Ada · Backend.");
        roster.LeadBranchId.ShouldBe(ada);
        roster.Parent.ShouldBe(new ChatTeamParent(chat.Id, "the main branch"));

        // Seen from the top team, Ada is a teammate who leads a team of one.
        var top = new ChatTeamRosterCalculator().Calculate(chat, [], chat.Id);
        top.Parent.ShouldBeNull();
        top.Members.Single(member => member.BranchId == ada).SubTeam.ShouldBe(1);
        top.Members[0].Name.ShouldBe("Lead");
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
