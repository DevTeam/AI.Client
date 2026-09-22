namespace AI.Client.Application.Runs;

using Contracts.Chats;

/// <summary>
/// Collects every branch id a chat has, including the main branch that shares the chat's id.
/// Lifting the rule out of call sites keeps the reconciliation step in
/// <see cref="IChatRunDispatcher"/> and the Host's startup reconcile from drifting apart.
/// </summary>
public interface IChatBranchIds
{
    IReadOnlySet<Guid> Collect(ChatDetails chat);
}
