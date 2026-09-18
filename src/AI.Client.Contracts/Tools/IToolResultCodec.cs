namespace AI.Client.Contracts.Tools;

/// <summary>Reads and writes the durable MCP tool-result representation used by chat history.</summary>
public interface IToolResultCodec
{
    string Write(ToolCallResult result);

    ToolCallResult Read(string? storedContent);
}
