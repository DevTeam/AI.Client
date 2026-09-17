namespace AI.Client.Web.Markdown;

public interface IMarkdownRenderer
{
    string Render(string markdown);

    /// <summary>
    /// Renders a short label — a question, a caption — where only inline markup makes sense.
    /// Emphasis, code spans and links come through; block structure does not, because the places
    /// this is used are sized for one line of text and a list or a table would break them.
    /// </summary>
    string RenderInline(string markdown);
}
