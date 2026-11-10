namespace AI.Search;

/// <summary>Recursive text search: one request in, everything reported out.</summary>
/// <remarks>
/// The engine follows the model of ripgrep: an ignore-aware walk of the tree feeds a pool of workers
/// that scan files concurrently, and the defaults are the ones people expect from it — Git ignore
/// files are respected, hidden entries and binary files are skipped, and matches are reported in
/// path order. Files come from <see cref="AI.Contracts.FileSystem.IFileSystem"/>, so the same engine
/// runs over a caller's directory grants and over an in-memory fake in a test.
/// </remarks>
public interface ISearchEngine
{
    /// <summary>Runs one search and returns everything it found, with the totals it reached.</summary>
    /// <exception cref="ArgumentException">The path or pattern is missing, or the regular expression is invalid.</exception>
    /// <exception cref="DirectoryNotFoundException">Nothing exists at the requested path.</exception>
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
