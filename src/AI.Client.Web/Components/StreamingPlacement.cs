namespace AI.Client.Web.Components;

using System.Text;

/// <summary>
/// Keeps the ambiguous beginning of a model response with the turn's intermediate activity long
/// enough for a streamed tool call to identify it as a preamble. A long answer is promoted after
/// a short grace period; a short answer stays in the fixed-height summary until it completes.
/// </summary>
public static class StreamingPlacement
{
    public static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(250);
    public const int EarlyReleaseCharacterCount = 160;

    public static bool ShouldPromote(TimeSpan elapsed, string content)
        => elapsed >= MinimumDelay && VisibleCharacterCount(content) >= EarlyReleaseCharacterCount;

    public static int VisibleCharacterCount(string content) =>
        content.EnumerateRunes().Count(rune => !Rune.IsWhiteSpace(rune));

    public static string RunningTitle(string content, string duration) =>
        $"{string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))} · {duration}";
}
