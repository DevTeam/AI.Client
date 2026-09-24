namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;
using System.Text.Json;

public sealed class ResourceModelProjection : IResourceModelProjection
{
    public string Project(string content, IReadOnlyList<ChatResourceRef>? references)
    {
        if (references is null or { Count: 0 }) return content;
        var lines = references.Select(item =>
            $"- {item.Kind.ToString().ToLowerInvariant()}: {JsonSerializer.Serialize(item.Path)} [resource {item.Id}; live path; contents not loaded]");
        return string.Join('\n', (new[] { content, "Attached workspace references:" }).Concat(lines)
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
