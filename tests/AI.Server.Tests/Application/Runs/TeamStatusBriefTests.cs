namespace AI.Application.Tests.Runs;

using AI.Application.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using Shouldly;
using Xunit;

public sealed class TeamStatusBriefTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static (ChatDetails Chat, Guid Ada, Guid Bo) Team()
    {
        var chatId = Guid.NewGuid();
        var lead = new MessageSender(chatId, chatId, "decision");
        var task = new ChatMessageView(Guid.NewGuid(), null, "User", "Build the export", T0);
        var adaBrief = new ChatMessageView(Guid.NewGuid(), task.Id, "User", "You are Ada.", T0.AddSeconds(1), Sender: lead);
        var boBrief = new ChatMessageView(Guid.NewGuid(), task.Id, "User", "You are Bo.", T0.AddSeconds(2), Sender: lead);
        var done = new ChatMessageView(Guid.NewGuid(), task.Id, "User", "Endpoint ready\ndetails", T0.AddSeconds(3),
            Sender: new MessageSender(chatId, adaBrief.Id, "done"));
        var question = new ChatMessageView(Guid.NewGuid(), done.Id, "User", "Which delimiter?", T0.AddSeconds(4),
            Sender: new MessageSender(chatId, boBrief.Id, "question"));
        return (new ChatDetails(chatId, Guid.NewGuid(), "Team", T0, T0, 1, null, [task, adaBrief, boBrief, done, question],
        [
            new ChatBranchView(chatId, question.Id, "Team"),
            new ChatBranchView(adaBrief.Id, adaBrief.Id, "Ada · Backend", chatId, adaBrief.Id, Member: new TeamMember("Ada", "Backend", "teal")),
            new ChatBranchView(boBrief.Id, boBrief.Id, "Bo · Tests", chatId, boBrief.Id, Member: new TeamMember("Bo", "Tests", "amber"))
        ]), adaBrief.Id, boBrief.Id);
    }

    [Fact]
    public void TheLeadShouldReadEveryTeammatesStateLatestReportAndOpenQuestion()
    {
        var (chat, ada, bo) = Team();
        var runs = new[] { new ChatRunSnapshot(chat.ProjectId, chat.Id, bo, ChatRunStatus.Generating, "", [], false, null, 1) };

        var status = new TeamStatusBrief(new ChatTeamRosterCalculator()).Describe(chat, chat.Id, runs)!;

        status.ShouldStartWith("Team status as this turn starts, from the application (1 of 2 done, 1 waiting for you).");
        status.ShouldContain($"- Ada · Backend (branchId {ada}): done; latest report done: \"Endpoint ready\"");
        status.ShouldContain($"- Bo · Tests (branchId {bo}): working; latest report question: \"Which delimiter?\"; "
            + "waiting for your answer to its question: \"Which delimiter?\"");
    }

    [Fact]
    public void OnlyTheLeadOfATeamGetsAStatus()
    {
        var (chat, ada, _) = Team();
        var plainId = Guid.NewGuid();
        var plain = new ChatDetails(plainId, Guid.NewGuid(), "Plain", T0, T0, 1, null, [], [new ChatBranchView(plainId, null, "Plain")]);
        var brief = new TeamStatusBrief(new ChatTeamRosterCalculator());

        brief.Describe(chat, ada, []).ShouldBeNull();
        brief.Describe(plain, plainId, []).ShouldBeNull();
    }
}
