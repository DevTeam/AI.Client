namespace AI.Application.Runs;

using System.Text;
using Contracts.Chats;
using Contracts.Runs;

/// <summary>
/// Writes the roster the team widget shows as text the lead reads before its first step. The same
/// calculator builds both, so the person and the lead see one state of the team.
/// </summary>
public sealed class TeamStatusBrief(IChatTeamRosterCalculator roster) : ITeamStatusBrief
{
    public string? Describe(ChatDetails chat, Guid branchId, IReadOnlyList<ChatRunSnapshot> runs)
    {
        var team = roster.Calculate(chat, runs, branchId);
        if (team.Members.Count == 0 || team.Members[0].BranchId != branchId) return null;
        if (!team.IsTeam) return null;
        var text = new StringBuilder()
            .Append("Team status as this turn starts, from the application (")
            .Append(team.Done).Append(" of ").Append(team.Teammates).Append(" done");
        if (team.Open > 0) text.Append(", ").Append(team.Open).Append(" waiting for you");
        text.Append("). It is the team's current state: do not read this branch or the teammates' branches to rebuild it.");
        foreach (var member in team.Members.Where(member => !member.IsLead))
        {
            text.Append("\n- ").Append(member.Name).Append(" · ").Append(member.Role)
                .Append(" (branchId ").Append(member.BranchId).Append("): ").Append(State(member.State));
            if (member.LastReport is { } report)
                text.Append("; latest report ").Append(report.Intent ?? "without intent")
                    .Append(": \"").Append(report.Text).Append('"');
            else text.Append("; no report yet");
            if (member.Open is { } open)
                text.Append("; waiting for your answer to its ").Append(open.Intent)
                    .Append(": \"").Append(open.Text).Append('"');
        }
        return text.ToString();
    }

    private static string State(ChatTeamMemberState state) => state switch
    {
        ChatTeamMemberState.Working => "working",
        ChatTeamMemberState.NeedsPerson => "waiting for the person (a confirmation or a question)",
        ChatTeamMemberState.Stopped => "stopped (failed, interrupted or paused)",
        ChatTeamMemberState.Done => "done",
        _ => "idle"
    };
}
