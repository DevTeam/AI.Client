namespace AI.Search.Matchers;

/// <summary>Finds the matches of one pattern in the bytes of one file.</summary>
internal interface IContentScanner
{
    /// <summary>
    /// Scans the content and appends at most <paramref name="limit"/> matches to
    /// <paramref name="matches"/>; zero means no limit. Returns how many matching lines the file
    /// holds, which is more than were appended when the limit cut them short. With
    /// <paramref name="firstOnly"/> the scan stops at the first match, which is all a file listing
    /// needs.
    /// </summary>
    int Scan(byte[] buffer, int length, int limit, bool firstOnly, List<SearchMatch> matches);
}
