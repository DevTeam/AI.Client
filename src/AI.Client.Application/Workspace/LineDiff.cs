namespace AI.Client.Application.Workspace;

using System.Text;

/// <summary>
/// Line-level difference between two versions of a file: how many lines were added and removed
/// net, plus a unified diff of the same comparison.
/// </summary>
/// <remarks>
/// Uses Myers' O(ND) algorithm, whose cost scales with the size of the difference rather than the
/// size of the files. That matches the case at hand — an agent edits a few lines of a file that is
/// otherwise unchanged — and keeps a large, barely-touched file cheap. When the difference turns
/// out to be genuinely large the search is abandoned at <see cref="MaxDifference"/> and the result
/// is reported as a wholesale replacement, marked approximate rather than silently wrong.
/// </remarks>
public sealed class LineDiff : ILineDiff
{
    /// <summary>Give up past this many differing lines and report a replacement instead.</summary>
    public const int MaxDifference = 5000;

    /// <summary>Lines of unchanged context kept around each hunk of the unified diff.</summary>
    private const int ContextLines = 3;

    /// <summary>A unified diff longer than this is truncated: nobody reads a megabyte of patch.</summary>
    private const int MaxDiffCharacters = 64 * 1024;

    public sealed record Result(int Additions, int Deletions, string Diff, bool IsExact);

    /// <summary>
    /// Compares two texts. Line endings are normalized for the comparison only — neither input is
    /// written anywhere, so a file's own CRLF/LF style is never disturbed by measuring it.
    /// </summary>
    public Result Compare(string? before, string? after)
    {
        var source = Split(before);
        var target = Split(after);

        if (source.Length == 0 && target.Length == 0) return new Result(0, 0, string.Empty, true);

        var script = Trace(source, target);
        if (script is null)
        {
            // Too different to trace within the cap: the honest summary is "all of it".
            return new Result(target.Length, source.Length, string.Empty, false);
        }

        var additions = script.Count(operation => operation.Kind == EditKind.Insert);
        var deletions = script.Count(operation => operation.Kind == EditKind.Delete);
        return new Result(additions, deletions, Unified(script), true);
    }

    private static string[] Split(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.ReplaceLineEndings("\n").Split('\n');

    private enum EditKind { Keep, Insert, Delete }

    private readonly record struct Edit(EditKind Kind, string Line);

    /// <summary>
    /// Myers' greedy algorithm, recording the furthest-reaching path at each edit distance so the
    /// edit script can be walked back once the end is reached. Returns null if the two texts differ
    /// by more than <see cref="MaxDifference"/> lines.
    /// </summary>
    private static List<Edit>? Trace(string[] source, string[] target)
    {
        var n = source.Length;
        var m = target.Length;
        var max = Math.Min(n + m, MaxDifference);
        var offset = max;
        var v = new int[2 * max + 1];
        var traces = new List<int[]>(Math.Min(max, 64));

        for (var d = 0; d <= max; d++)
        {
            traces.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[k - 1 + offset] < v[k + 1 + offset])) x = v[k + 1 + offset];
                else x = v[k - 1 + offset] + 1;
                var y = x - k;
                while (x < n && y < m && string.Equals(source[x], target[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }
                v[k + offset] = x;
                if (x >= n && y >= m) return Backtrack(source, target, traces, d, offset);
            }
        }
        return null;
    }

    private static List<Edit> Backtrack(string[] source, string[] target, List<int[]> traces, int d, int offset)
    {
        var script = new List<Edit>();
        var x = source.Length;
        var y = target.Length;
        for (var step = d; step > 0; step--)
        {
            var v = traces[step];
            var k = x - y;
            int previousK;
            if (k == -step || (k != step && v[k - 1 + offset] < v[k + 1 + offset])) previousK = k + 1;
            else previousK = k - 1;
            var previousX = v[previousK + offset];
            var previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                script.Add(new Edit(EditKind.Keep, source[x - 1]));
                x--;
                y--;
            }
            if (y > previousY) script.Add(new Edit(EditKind.Insert, target[y - 1]));
            else if (x > previousX) script.Add(new Edit(EditKind.Delete, source[x - 1]));
            x = previousX;
            y = previousY;
        }
        while (x > 0 && y > 0)
        {
            script.Add(new Edit(EditKind.Keep, source[x - 1]));
            x--;
            y--;
        }
        script.Reverse();
        return script;
    }

    /// <summary>Renders the edit script as a unified diff, hunks only, without file headers.</summary>
    private static string Unified(List<Edit> script)
    {
        var text = new StringBuilder();
        var sourceLine = 1;
        var targetLine = 1;
        var index = 0;
        while (index < script.Count)
        {
            if (script[index].Kind == EditKind.Keep)
            {
                if (script[index].Kind == EditKind.Keep) { sourceLine++; targetLine++; }
                index++;
                continue;
            }

            // Walk back over the context preceding this hunk, then forward to its end plus context,
            // absorbing any further change that falls within one context window of it.
            var start = index;
            var leading = 0;
            while (start > 0 && script[start - 1].Kind == EditKind.Keep && leading < ContextLines) { start--; leading++; }

            var end = index;
            var trailing = 0;
            while (end < script.Count && trailing <= ContextLines)
            {
                if (script[end].Kind == EditKind.Keep) trailing++;
                else trailing = 0;
                end++;
            }

            var hunkSourceStart = sourceLine - leading;
            var hunkTargetStart = targetLine - leading;
            var sourceCount = 0;
            var targetCount = 0;
            var body = new StringBuilder();
            for (var cursor = start; cursor < end; cursor++)
            {
                var edit = script[cursor];
                switch (edit.Kind)
                {
                    case EditKind.Keep:
                        body.Append(' ').AppendLine(edit.Line);
                        sourceCount++;
                        targetCount++;
                        break;
                    case EditKind.Insert:
                        body.Append('+').AppendLine(edit.Line);
                        targetCount++;
                        break;
                    case EditKind.Delete:
                        body.Append('-').AppendLine(edit.Line);
                        sourceCount++;
                        break;
                }
            }
            text.Append("@@ -").Append(hunkSourceStart).Append(',').Append(sourceCount)
                .Append(" +").Append(hunkTargetStart).Append(',').Append(targetCount).AppendLine(" @@");
            text.Append(body);
            if (text.Length > MaxDiffCharacters)
            {
                text.Length = MaxDiffCharacters;
                text.AppendLine().Append("… diff truncated");
                return text.ToString();
            }

            for (var cursor = index; cursor < end; cursor++)
            {
                switch (script[cursor].Kind)
                {
                    case EditKind.Keep: sourceLine++; targetLine++; break;
                    case EditKind.Insert: targetLine++; break;
                    case EditKind.Delete: sourceLine++; break;
                }
            }
            index = end;
        }
        return text.ToString();
    }
}
