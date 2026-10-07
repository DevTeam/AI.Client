namespace AI.Application.Runs;

using Contracts.Chats;
using Contracts.Runs;

/// <summary>
/// The team's state as a lead's turn starts, for the model: one line per teammate. Null for any
/// other run; see docs/34-asides-and-team-messages.md.
/// </summary>
public interface ITeamStatusBrief
{
    string? Describe(ChatDetails chat, Guid branchId, IReadOnlyList<ChatRunSnapshot> runs);
}
