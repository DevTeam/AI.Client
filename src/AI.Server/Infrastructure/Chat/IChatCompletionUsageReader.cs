namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using System.Text.Json;

/// <summary>Reads the <c>usage</c> object an OpenAI-compatible response may carry.</summary>
public interface IChatCompletionUsageReader
{
    /// <summary>The usage <paramref name="response"/> reports, or null when it reports none.</summary>
    ChatCompletionUsage? Read(JsonElement response);
}
