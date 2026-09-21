namespace AI.Client.Web.Chats;

using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;

/// <summary>Applies a contiguous run-message tail to an already loaded chat.</summary>
public interface IChatMessageDeltaMerger
{
    bool TryApply(ChatDetails current, ChatRunSnapshot run, out ChatDetails updated);
}
