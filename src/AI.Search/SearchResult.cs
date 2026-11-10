namespace AI.Search;

/// <summary>
/// One reported occurrence. <paramref name="Column"/> is 1-based and counted in characters, and
/// <paramref name="Text"/> is the line the match is on — cut down to a window around the match when
/// the line is very long, which a minified file makes necessary.
/// </summary>
public sealed record SearchMatch(int Line, int Column, string Text);

/// <summary>
/// One file the search reported: its path relative to the searched root, how many matches it holds,
/// and — in line output — the matches themselves, capped by the request.
/// </summary>
public sealed record FoundFile(string Path, int MatchCount, IReadOnlyList<SearchMatch> Matches);

/// <summary>What the search did, independent of what it found.</summary>
/// <param name="FilesSearched">Files opened and scanned, an empty one included.</param>
/// <param name="FilesSkipped">Files left unread: binary, too large, or not readable.</param>
/// <param name="FilesWithMatches">Files that contributed at least one match.</param>
/// <param name="BytesSearched">Bytes scanned, which is the input size the timings are about.</param>
/// <param name="Matches">Matches reported, which stops at <see cref="SearchRequest.MaxMatches"/>.</param>
public sealed record SearchTotals(int FilesSearched, int FilesSkipped, int FilesWithMatches, long BytesSearched, int Matches);

/// <summary>The complete outcome of one search.</summary>
/// <param name="Path">The absolute root that was searched.</param>
/// <param name="Pattern">The pattern as the caller wrote it.</param>
/// <param name="Files">Files with matches, in path order.</param>
/// <param name="Totals">What the search did and how much of it.</param>
/// <param name="Truncated">
/// Whether the search stopped early because <see cref="SearchRequest.MaxMatches"/> was reached; the
/// result is then a prefix of what a complete search would have reported.
/// </param>
/// <param name="Duration">Time from the request to the finished result.</param>
public sealed record SearchResult(
    string Path,
    string Pattern,
    IReadOnlyList<FoundFile> Files,
    SearchTotals Totals,
    bool Truncated,
    TimeSpan Duration);
