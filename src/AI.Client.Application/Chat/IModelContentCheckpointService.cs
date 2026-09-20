namespace AI.Client.Application.Chat;

using Contracts.Chat;
using Tools;

public interface IModelContentCheckpointService
{
    IDisposable Begin(ToolRunContext run, Func<string, CancellationToken, Task<string>> summarize);
    void Update(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);
    IReadOnlyList<ChatCompletionMessage> Apply(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);
    ModelContentCompactionPreview Preview(ToolRunContext run);
    Task<ModelContentCompactionResult> CompactAsync(ToolRunContext run, int targetTokens, CancellationToken cancellationToken);
    bool Reset(ToolRunContext run);
}

public sealed record ModelContentCompactionPreview(int CoveredMessages, long SourceCharacters, bool CanCompact);

public sealed record ModelContentCompactionResult(
    int CoveredMessages,
    long SourceCharacters,
    int SummaryCharacters,
    bool Applied,
    string Guidance);
