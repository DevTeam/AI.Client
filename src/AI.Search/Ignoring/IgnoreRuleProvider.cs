namespace AI.Search.Ignoring;

using AI.Contracts.FileSystem;

/// <summary>
/// Reads ignore files as the walk meets them. The files of every directory from the searched root
/// up to its repository root apply before the walk starts; below that, each directory contributes
/// its own files when the walk enters it.
/// </summary>
internal sealed class IgnoreRuleProvider(IFileSystem files, IPath paths)
{
    private readonly IFileSystem _files = files;
    private readonly IPath _paths = paths;

    /// <summary>
    /// The rules in force at <paramref name="root"/>. A repository boundary is honoured the way Git
    /// honours it: <c>.gitignore</c> files above the repository root are not the repository's, so
    /// they are not read, while <c>.ignore</c> and <c>.rgignore</c> are read at any level because
    /// they belong to the directory they are in rather than to version control.
    /// </summary>
    public async Task<IgnoreRules> ForRootAsync(string root, CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        string? repository = null;
        for (var current = root; current is not null; current = _paths.GetDirectoryName(current))
        {
            directories.Add(current);
            var marker = _paths.Combine(current, ".git");
            if (await _files.DirectoryExistsAsync(marker, cancellationToken) || await _files.FileExistsAsync(marker, cancellationToken))
            {
                repository = current;
                break;
            }
        }

        directories.Reverse();
        var rules = IgnoreRules.None;
        foreach (var directory in directories)
        {
            // Above the repository only the ripgrep-specific files apply; inside it, all of them.
            var names = repository is null || _paths.IsInside(directory, repository, recursive: true)
                ? IgnoreRules.FileNames
                : [".ignore", ".rgignore"];
            // The repository's own excludes come before the .gitignore files of the same directory,
            // which is the order Git applies them in: a .gitignore rule overrides info/exclude.
            if (directory == repository && await ExcludeFileAsync(directory, cancellationToken) is { } exclude)
            {
                rules = rules.And(await ReadFileAsync(exclude, directory, cancellationToken));
            }

            rules = rules.And(await ReadAsync(directory, names, cancellationToken));
        }

        return rules;
    }

    /// <summary>The rules the directory's own files state, to be applied by the walk below it.</summary>
    public async Task<IgnoreRules> ForDirectoryAsync(string directory, CancellationToken cancellationToken) =>
        await ReadAsync(directory, IgnoreRules.FileNames, cancellationToken);

    private async Task<IgnoreRules> ReadAsync(string directory, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var rules = IgnoreRules.None;
        foreach (var name in names)
        {
            var path = _paths.Combine(directory, name);
            if (!await _files.FileExistsAsync(path, cancellationToken))
            {
                continue;
            }

            rules = rules.And(await ReadFileAsync(path, directory, cancellationToken));
        }

        return rules;
    }

    /// <summary>
    /// The rules one ignore file states, anchored to <paramref name="directory"/>; an unreadable file
    /// states none, because an ignore file nobody can read hides nothing from the walk.
    /// </summary>
    private async Task<IgnoreRules> ReadFileAsync(string path, string directory, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        try
        {
            await foreach (var line in _files.ReadLinesAsync(path, cancellationToken))
            {
                lines.Add(line);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return IgnoreRules.None;
        }

        return IgnoreRules.Parse(lines, directory, _paths.Comparison);
    }

    /// <summary>
    /// The repository's <c>info/exclude</c>, or null when the repository has none. A linked worktree's
    /// <c>.git</c> is a file naming its own git directory, whose <c>commondir</c> names the shared one
    /// that holds <c>info</c>, so a worktree is ignored exactly as its main checkout is.
    /// </summary>
    private async Task<string?> ExcludeFileAsync(string repository, CancellationToken cancellationToken)
    {
        var marker = _paths.Combine(repository, ".git");
        var gitDirectory = marker;
        if (!await _files.DirectoryExistsAsync(marker, cancellationToken))
        {
            if (await _files.ReadTextAsync(marker, cancellationToken) is not { } pointer)
            {
                return null;
            }

            var line = pointer.Trim();
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return null;
            }

            gitDirectory = _paths.GetFullPath(line["gitdir:".Length..].Trim(), repository);
            var common = _paths.Combine(gitDirectory, "commondir");
            if (await _files.ReadTextAsync(common, cancellationToken) is { } shared && shared.Trim().Length > 0)
            {
                gitDirectory = _paths.GetFullPath(shared.Trim(), gitDirectory);
            }
        }

        var exclude = _paths.Combine(_paths.Combine(gitDirectory, "info"), "exclude");
        return await _files.FileExistsAsync(exclude, cancellationToken) ? exclude : null;
    }
}
