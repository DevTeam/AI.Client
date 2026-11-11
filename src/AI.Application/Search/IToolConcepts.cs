namespace AI.Application.Search;

using Tools;

/// <summary>
/// Language-independent capability concepts. Tool names and descriptions are English, so a request
/// in another language matched none of them, and a small model had to find even its file tools
/// through an extra search step. The concept keywords of every supported language are kept in
/// <c>ToolConcepts.*.resx</c>; the concepts each built-in and application tool serves are listed in
/// <see cref="ToolConcepts"/>.
/// </summary>
public interface IToolConcepts
{
    /// <summary>The concepts whose keywords occur in the text, in any supported language.</summary>
    IReadOnlySet<string> Find(string text);

    /// <summary>The concepts a built-in or application tool serves; empty for any other tool.</summary>
    IReadOnlySet<string> ServedBy(AgentTool tool);
}
