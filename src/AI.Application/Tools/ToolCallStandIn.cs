namespace AI.Application.Tools;

using System.Text.Json;
using Contracts.Chat;
using Contracts.Tools;

/// <summary>
/// Takes apart a call made through <see cref="ToolRef.CallToolName"/>. A tool found by discovery is
/// called through the stand-in so that its definition need not join the request's tool list: a
/// list that changes moves the start of every later request, and the provider's cached prefix with it.
/// </summary>
public interface IToolCallStandIn
{
    /// <summary>
    /// The call the model meant: a call to the stand-in becomes a call, under the same id, to the
    /// permitted tool it names; any other call, or one that cannot be read, is returned unchanged.
    /// </summary>
    ChatToolCall Resolve(ChatToolCall toolCall, IReadOnlyList<AgentTool> permitted);
}

public sealed class ToolCallStandIn : IToolCallStandIn
{
    public ChatToolCall Resolve(ChatToolCall toolCall, IReadOnlyList<AgentTool> permitted)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        var call = toolCall;
        ArgumentNullException.ThrowIfNull(permitted);
        if (call.Name != ToolRef.CallToolName) return call;
        JsonElement input;
        try
        {
            input = JsonElement.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
        }
        catch (JsonException)
        {
            return call;
        }
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("name", out var nameValue)
            || nameValue.ValueKind != JsonValueKind.String || nameValue.GetString() is not { Length: > 0 } given)
            return call;
        var name = given.Trim();
        // Named as tool_search listed it, or by the bare name a model sometimes drops the prefix to.
        var resolved = permitted.FirstOrDefault(tool => tool.ModelDefinition.Name == name)?.ModelDefinition.Name
                       ?? permitted.Select(tool => tool.ModelDefinition.Name)
                           .FirstOrDefault(full => full.EndsWith("__" + name, StringComparison.Ordinal))
                       ?? name;
        if (resolved == ToolRef.CallToolName) return call;
        return call with { Name = resolved, Arguments = Arguments(input) };
    }

    /// <summary>
    /// The arguments object, also when a model sent it as a JSON string or spread its fields beside
    /// the name instead of nesting them.
    /// </summary>
    private static string Arguments(JsonElement input)
    {
        if (input.TryGetProperty("arguments", out var arguments))
        {
            if (arguments.ValueKind == JsonValueKind.Object) return arguments.GetRawText();
            if (arguments.ValueKind == JsonValueKind.String && arguments.GetString() is { Length: > 0 } text)
            {
                try
                {
                    if (JsonElement.Parse(text) is { ValueKind: JsonValueKind.Object } parsed) return parsed.GetRawText();
                }
                catch (JsonException)
                {
                    // Left for the tool's own validation to report.
                }
                return text;
            }
            if (arguments.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "{}";
            return arguments.GetRawText();
        }
        var spread = input.EnumerateObject().Where(property => property.Name != "name")
            .ToDictionary(property => property.Name, property => property.Value);
        return JsonSerializer.Serialize(spread);
    }
}
