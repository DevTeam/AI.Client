namespace AI.Search.Matchers;

using System.Text;

/// <summary>
/// Scans the bytes of a file line by line. Both a literal and a regular expression search go
/// through it, so line splitting, decoding and the reported column are decided in one place and
/// behave the same for both.
/// </summary>
/// <remarks>
/// A file is decoded as UTF-8 line by line into a buffer that is reused for the next line: a search
/// reports text, not bytes, and a column in characters is what a reader can act on. The buffer
/// costs nothing when a file is small and never grows past the longest line of the file.
/// </remarks>
internal sealed class ContentScanner(ILineMatcher matcher) : IContentScanner
{
    private readonly ILineMatcher _matcher = matcher;
    private readonly byte[]? _required = matcher.RequiredBytes;
    private char[] _buffer = [];

    public int Scan(byte[] buffer, int length, int limit, bool firstOnly, List<SearchMatch> matches)
    {
        var content = buffer.AsSpan(0, length);
        var count = 0;
        var number = 0;
        var position = 0;
        while (position < content.Length)
        {
            var end = content[position..].IndexOf((byte)'\n');
            var next = end < 0 ? content.Length : position + end;
            var line = content[position..next];
            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            number++;
            position = next < content.Length ? next + 1 : content.Length;
            if (ScanLine(line, number, limit, matches))
            {
                count++;
                if (firstOnly)
                {
                    return count;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Matches one line and reports it unless the caller already has <paramref name="limit"/>
    /// matches from this file. Returns whether the line matched at all, so the count stays complete
    /// even when the reported list is capped.
    /// </summary>
    private bool ScanLine(ReadOnlySpan<byte> line, int number, int limit, List<SearchMatch> matches)
    {
        if (line.IsEmpty)
        {
            return false;
        }

        // A line without the bytes every match must hold cannot match, and the bytes are cheaper to
        // look for than decoding the line and running the expression on it.
        if (_required is not null && line.IndexOf(_required.AsSpan()) < 0)
        {
            return false;
        }

        if (_buffer.Length < line.Length)
        {
            _buffer = new char[Math.Max(line.Length, 256)];
        }

        var characters = _buffer.AsSpan(0, line.Length);
        var decoded = Encoding.UTF8.GetChars(line, characters);
        characters = characters[..decoded];
        var (index, length) = _matcher.Find(characters);
        if (index < 0)
        {
            return false;
        }

        if (limit == 0 || matches.Count < limit)
        {
            matches.Add(new SearchMatch(number, index + 1, MatchText.Window(characters, index, length)));
        }

        return true;
    }
}
