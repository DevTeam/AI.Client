namespace AI.Contracts.Workspace;

using System.Globalization;

/// <summary>
/// Reads the unified diff carried by a <see cref="FileChange"/> into typed lines, so the markup
/// renders a structure rather than re-deciding what each character prefix meant.
/// </summary>
/// <remarks>
/// The input is this application's own output, but it is still parsed defensively: it can be
/// truncated mid-line at the size cap, and a chat file can be hand-edited. Anything unrecognized
/// becomes a <see cref="DiffLineKind.Note"/> rather than being dropped or throwing, because a
/// malformed hunk must not be able to hide the rest of a file's changes.
/// </remarks>
public sealed class UnifiedDiff : IUnifiedDiffParser
{
    public IReadOnlyList<DiffLine> Parse(string? diff)
    {
        if (string.IsNullOrEmpty(diff)) return [];

        var lines = new List<DiffLine>();
        var oldLine = 0;
        var newLine = 0;
        var oldRemaining = 0;
        var newRemaining = 0;

        foreach (var raw in diff.ReplaceLineEndings("\n").Split('\n'))
        {
            if (raw.Length == 0) continue;

            if (raw.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                oldRemaining = newRemaining = 0;
                lines.Add(new DiffLine(DiffLineKind.Note, raw));
                continue;
            }

            if (oldRemaining == 0 && newRemaining == 0
                && (raw.StartsWith("--- ", StringComparison.Ordinal) || raw.StartsWith("+++ ", StringComparison.Ordinal)))
            {
                lines.Add(new DiffLine(DiffLineKind.Note, raw));
                continue;
            }

            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                (oldLine, newLine) = ReadHunkStarts(raw);
                oldRemaining = ReadCount(raw, '-');
                newRemaining = ReadCount(raw, '+');
                lines.Add(new DiffLine(DiffLineKind.Hunk, raw));
                continue;
            }

            switch (raw[0])
            {
                case ' ':
                    lines.Add(new DiffLine(DiffLineKind.Context, raw[1..], oldLine, newLine));
                    oldLine++;
                    newLine++;
                    oldRemaining = Math.Max(0, oldRemaining - 1);
                    newRemaining = Math.Max(0, newRemaining - 1);
                    break;
                case '+':
                    lines.Add(new DiffLine(DiffLineKind.Added, raw[1..], null, newLine));
                    newLine++;
                    newRemaining = Math.Max(0, newRemaining - 1);
                    break;
                case '-':
                    lines.Add(new DiffLine(DiffLineKind.Removed, raw[1..], oldLine, null));
                    oldLine++;
                    oldRemaining = Math.Max(0, oldRemaining - 1);
                    break;
                default:
                    // The truncation trailer, or anything the format did not promise.
                    lines.Add(new DiffLine(DiffLineKind.Note, raw));
                    break;
            }
        }

        return lines;
    }

    /// <summary>
    /// Reads the two starting line numbers out of <c>@@ -12,7 +12,8 @@</c>. A header this cannot
    /// read leaves the counters where they were, so the numbering stays plausible instead of
    /// jumping to zero mid-file.
    /// </summary>
    private static (int Old, int New) ReadHunkStarts(string header) =>
        (ReadStart(header, '-') ?? 1, ReadStart(header, '+') ?? 1);

    private static int? ReadStart(string header, char marker)
    {
        var at = header.IndexOf(marker);
        if (at < 0) return null;
        var start = at + 1;
        var end = start;
        while (end < header.Length && char.IsAsciiDigit(header[end])) end++;
        return end > start && int.TryParse(header.AsSpan(start, end - start), CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int ReadCount(string header, char marker)
    {
        var at = header.IndexOf(marker);
        if (at < 0) return int.MaxValue;
        var start = at + 1;
        while (start < header.Length && char.IsAsciiDigit(header[start])) start++;
        if (start >= header.Length || header[start] != ',') return 1;
        start++;
        var end = start;
        while (end < header.Length && char.IsAsciiDigit(header[end])) end++;
        return int.TryParse(header.AsSpan(start, end - start), CultureInfo.InvariantCulture, out var count)
            ? count : int.MaxValue;
    }
}

/// <param name="Text">The line's content, with the diff's own prefix character already removed.</param>
/// <param name="OldLine">Its number in the file as it was; null for an added line.</param>
/// <param name="NewLine">Its number in the file as it stands; null for a removed line.</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldLine = null, int? NewLine = null);

public enum DiffLineKind
{
    Context,
    Added,
    Removed,

    /// <summary>A <c>@@ … @@</c> header: where in the file the lines below it sit.</summary>
    Hunk,

    /// <summary>Something about the diff rather than in it — the truncation trailer, say.</summary>
    Note,
}
