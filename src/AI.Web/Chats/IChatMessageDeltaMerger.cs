namespace AI.Web.Chats;

using AI.Contracts.Chats;
using AI.Contracts.Runs;

/// <summary>Applies a contiguous run-message tail to an already loaded chat.</summary>
public interface IChatMessageDeltaMerger
{
    bool TryApply(ChatDetails current, ChatRunSnapshot run, out ChatDetails updated);
}
