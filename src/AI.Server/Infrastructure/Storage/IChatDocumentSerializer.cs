namespace AI.Infrastructure.Storage;

using AI.Application.Chats;
using AI.Domain.Chats;

/// <summary>
/// Reads and writes the on-disk chat document and its summary manifest. The serializer is
/// stateless, but going through an interface keeps the storage adapter thin and lets tests
/// substitute a serializer for one that has already been pointed at a fixture directory.
/// </summary>
public interface IChatDocumentSerializer
{
    string Serialize(ChatThread chat, long revision);

    string SerializeSummary(ChatThread chat, long revision);

    StoredChat Deserialize(string json);

    StoredChatSummary DeserializeSummary(string json);
}
