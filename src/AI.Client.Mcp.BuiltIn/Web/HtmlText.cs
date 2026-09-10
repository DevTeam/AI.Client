namespace AI.Client.Mcp.BuiltIn.Web;

using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// Lightweight HTML to Markdown extraction. This is a tag-level transformation, not a DOM implementation:
/// it drops non-content elements, keeps headings, links, lists and code, and then strips the remaining markup.
/// </summary>
public static partial class HtmlText
{
    public static string ToMarkdown(string html)
    {
        var text = Noise().Replace(html, " ");
        if (MainRegion().Match(text) is { Success: true } main)
        {
            text = main.Groups["body"].Value;
        }
        else if (Body().Match(text) is { Success: true } body)
        {
            text = body.Groups["body"].Value;
        }

        text = Heading().Replace(text, match => "\n\n" + new string('#', int.Parse(match.Groups["level"].Value, System.Globalization.CultureInfo.InvariantCulture)) + " ");
        text = HeadingEnd().Replace(text, "\n\n");
        text = Anchor().Replace(text, "[${text}](${href})");
        text = Strong().Replace(text, "**");
        text = Emphasis().Replace(text, "*");
        text = Code().Replace(text, "`");
        text = Pre().Replace(text, "\n```\n");
        text = ListItem().Replace(text, "\n- ");
        text = Break().Replace(text, "\n");
        text = Block().Replace(text, "\n\n");
        text = Tag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = Trailing().Replace(text, "\n");
        return Blank().Replace(text, "\n\n").Trim();
    }

    [GeneratedRegex(@"<(script|style|noscript|svg|template|iframe)\b[^>]*>.*?</\1\s*>|<!--.*?-->|<head\b[^>]*>.*?</head\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex Noise();

    [GeneratedRegex(@"<(?:main|article)\b[^>]*>(?<body>.*?)</(?:main|article)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex MainRegion();

    [GeneratedRegex(@"<body\b[^>]*>(?<body>.*?)</body\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex Body();

    [GeneratedRegex(@"<h(?<level>[1-6])\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Heading();

    [GeneratedRegex(@"</h[1-6]\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex HeadingEnd();

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*[""'](?<href>[^""'<>\s]{1,2048})[""'][^>]*>(?<text>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"</?(?:strong|b)\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Strong();

    [GeneratedRegex(@"</?(?:em|i)\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"</?code\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Code();

    [GeneratedRegex(@"</?pre\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Pre();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<br\b[^>]*/?>|<hr\b[^>]*/?>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Break();

    [GeneratedRegex(@"</(?:p|div|section|tr|ul|ol|li|table|blockquote|figure|header|footer|nav|aside)\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Block();

    [GeneratedRegex(@"<[^>]{0,4096}>", RegexOptions.Singleline, 2000)]
    private static partial Regex Tag();

    [GeneratedRegex(@"[^\S\r\n]+", RegexOptions.None, 2000)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^\S\n]*\n", RegexOptions.None, 2000)]
    private static partial Regex Trailing();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, 2000)]
    private static partial Regex Blank();
}
