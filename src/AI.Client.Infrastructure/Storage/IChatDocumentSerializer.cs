using AI.Client.Application.Chats;
using AI.Client.Domain.Chats;

namespace AI.Client.Infrastructure.Storage;

public interface IChatDocumentSerializer
{
    string Serialize(ChatThread chat, long revision);
    StoredChat Deserialize(string json);
}
