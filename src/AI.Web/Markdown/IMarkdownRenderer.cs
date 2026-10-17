namespace AI.Web.Markdown;

public interface IMarkdownRenderer
{
    string Render(string markdown);

    /// <summary>
    /// Renders a message the user typed. Each line break in it stays a line break: the composer
    /// shows one on Enter, and folding it into a space as markdown does would lose the layout the
    /// user saw while typing. A model's reply keeps the markdown rule and goes through <see cref="Render"/>.
    /// </summary>
    string RenderUser(string markdown);

    /// <summary>
    /// Renders a short label — a question, a caption — where only inline markup makes sense.
    /// Emphasis, code spans and links come through; block structure does not, because the places
    /// this is used are sized for one line of text and a list or a table would break them.
    /// </summary>
    string RenderInline(string markdown);
}
