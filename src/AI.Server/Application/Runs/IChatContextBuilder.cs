namespace AI.Application.Runs;

using Chat;
using Contracts.Chats;

/// <summary>
/// Builds the ordered list of messages that go to the model for a given branch head: walks the
/// message ancestry from the head back to the root, repairs interrupted tool exchanges, and keeps
/// the model's call order intact.
/// </summary>
public interface IChatContextBuilder
{
    IReadOnlyList<ChatCompletionMessage> Build(ChatDetails chat, Guid headId, Guid? branchId = null);
    Task<IReadOnlyList<ChatCompletionMessage>> BuildAsync(ChatDetails chat, Guid headId, CancellationToken cancellationToken,
        Guid? branchId = null);
}
