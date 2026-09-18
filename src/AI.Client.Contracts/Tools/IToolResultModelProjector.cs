namespace AI.Client.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// Builds the part of a tool result that may be returned to the model. Host and UI metadata is
/// deliberately absent from this contract, so it cannot enter model context through projection.
/// </summary>
public interface IToolResultModelProjector
{
    string Project(
        IReadOnlyList<ToolContent> content,
        JsonElement? structuredContent,
        bool isError);
}
