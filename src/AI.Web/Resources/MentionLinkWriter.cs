namespace AI.Web.Resources;

using System.Text;
using AI.Contracts.Resources;

public sealed class MentionLinkWriter(IResourcePresenter presenter) : IMentionLinkWriter
{
    private const string MarkdownPunctuation = "\\`*_{}[]()#+-.!<>|~";

    public string Link(string markdown, IReadOnlyList<ChatResourceRef>? resources)
    {
        var mentioned = resources?
            .Where(item => item.Mention is { Length: > 1 })
            // The longer link first: "@src/app/" must not be taken for the start of "@src/app/x.cs".
            .OrderByDescending(item => item.Mention!.Length)
            .ToArray() ?? [];
        if (mentioned.Length == 0 || markdown.Length == 0) return markdown;
        var result = new StringBuilder(markdown.Length + 64);
        var index = 0;
        while (index < markdown.Length)
        {
            var match = markdown[index] == '@' && StandsAlone(markdown, index)
                ? mentioned.FirstOrDefault(item => Matches(markdown, index, item.Mention!))
                : null;
            if (match is null)
            {
                // A code span is written as it is: a link inside it would show as its markdown.
                if (markdown[index] == '`') index = CopyCode(markdown, index, result);
                else result.Append(markdown[index++]);
                continue;
            }
            // The link shows the name; the token as written stays in its tooltip and in the message.
            result.Append('[').Append(Escape(presenter.Label(match))).Append("](<").Append(Target(match)).Append("> \"")
                .Append(Title(presenter.Hint(match))).Append("\"){.mention-link .mention-")
                .Append(match.Kind.ToString().ToLowerInvariant());
            if (presenter.Summary(match) is { } summary) result.Append(" data-summary=\"").Append(summary).Append('"');
            result.Append('}');
            index += match.Mention!.Length;
        }
        return result.ToString();
    }

    private static bool StandsAlone(string text, int index) => index == 0 || char.IsWhiteSpace(text[index - 1]) || text[index - 1] is '(' or '"';

    /// <summary>The link ends where the word does; punctuation a sentence puts after it may follow.</summary>
    private static bool Matches(string text, int index, string mention)
    {
        if (string.CompareOrdinal(text, index, mention, 0, mention.Length) != 0) return false;
        var end = index + mention.Length;
        return end == text.Length || char.IsWhiteSpace(text[end]) || text[end] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or '"';
    }

    private static int CopyCode(string text, int index, StringBuilder result)
    {
        var run = 0;
        while (index + run < text.Length && text[index + run] == '`') run++;
        var fence = new string('`', run);
        var close = text.IndexOf(fence, index + run, StringComparison.Ordinal);
        var end = close < 0 ? index + run : close + run;
        result.Append(text, index, end - index);
        return end;
    }

    private static string Target(ChatResourceRef reference) => reference.Kind is ChatResourceKind.File or ChatResourceKind.Directory
        ? FileUri(reference.Path) + (reference.Lines is { } lines ? $"#L{lines.Start}-L{lines.End}" : string.Empty)
        : $"#mention-{reference.Id}";

    /// <summary>A file URI by hand: in the browser <see cref="Uri"/> does not know a Windows path for one.</summary>
    private static string FileUri(string path)
    {
        var slashed = path.Replace('\\', '/');
        var encoded = string.Join('/', slashed.Split('/').Select(Uri.EscapeDataString));
        if (slashed.StartsWith("//", StringComparison.Ordinal)) return "file:" + encoded;
        if (slashed.Length >= 2 && slashed[1] == ':') return $"file:///{slashed[..2]}{encoded[(encoded.IndexOf('/', StringComparison.Ordinal) is var at and >= 0 ? at : encoded.Length)..]}";
        return "file://" + encoded;
    }

    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            if (MarkdownPunctuation.Contains(character, StringComparison.Ordinal)) escaped.Append('\\');
            escaped.Append(character);
        }
        return escaped.ToString();
    }

    /// <summary>
    /// A link title on one line: a raw line break would end a table row or a list item's line, so
    /// breaks go in as the entity markdown decodes in a title.
    /// </summary>
    private static string Title(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "&#10;", StringComparison.Ordinal);
}
