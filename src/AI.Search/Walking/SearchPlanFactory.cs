namespace AI.Search.Walking;

using AI.Contracts.FileSystem;
using AI.Search.Matchers;

/// <summary>Resolves a request against the file system and compiles its globs, once per search.</summary>
internal sealed class SearchPlanFactory(IFileSystem files, IPath paths, GlobSetFactory globs)
{
    private readonly IFileSystem _files = files;
    private readonly IPath _paths = paths;
    private readonly GlobSetFactory _globs = globs;

    /// <summary>
    /// The plan for the request.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path, the pattern or a glob is missing or malformed.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">Nothing exists at the requested path.</exception>
    public async Task<SearchPlan> CreateAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw new ArgumentException("A search needs a path.", nameof(request));
        }

        if (string.IsNullOrEmpty(request.Pattern))
        {
            throw new ArgumentException("A search needs a pattern.", nameof(request));
        }

        if (request.MaxMatches < 0 || request.MaxMatchesPerFile < 0 || request.MaxFileBytes < 0)
        {
            throw new ArgumentException("Search limits cannot be negative.", nameof(request));
        }

        var root = _paths.GetFullPath(request.Path);
        var isFile = await _files.FileExistsAsync(root, cancellationToken);
        if (!isFile && !await _files.DirectoryExistsAsync(root, cancellationToken))
        {
            throw new DirectoryNotFoundException($"Nothing exists at the search path: {root}");
        }

        // The walker asks the root for paths relative to it, and a file is its own root, so the
        // reported name of a single searched file is the file's name.
        return new SearchPlan(
            root,
            isFile,
            _globs.Create(request.Include),
            _globs.Create(request.Exclude),
            request.RespectIgnoreFiles,
            request.IncludeHidden,
            request.SkipBinary,
            request.MaxFileBytes,
            request.MaxMatches,
            request.MaxMatchesPerFile,
            request.Output,
            request.Parallelism > 0 ? request.Parallelism : Environment.ProcessorCount);
    }
}
