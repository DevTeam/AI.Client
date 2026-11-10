namespace AI.Search.Walking;

using System.Threading.Channels;
using AI.Contracts.FileSystem;
using AI.Search.Ignoring;

/// <summary>A file the walk decided to scan: its absolute path and its path relative to the root.</summary>
internal readonly record struct SearchCandidate(string Path, string RelativePath);

/// <summary>
/// Walks the tree and hands every file the request accepts to the scanning workers. The walk itself
/// runs on one thread — a directory costs one listing, and listing in parallel rarely pays for the
/// coordination — while the scanning it feeds runs on as many threads as the plan allows, which is
/// where a search actually spends its time.
/// </summary>
/// <remarks>
/// The walk is depth first and decides everything ripgrep decides on the way down: a hidden entry
/// unless hidden entries were asked for, an ignored entry, a directory the caller excluded, a link
/// (never followed, so a link that points back up the tree cannot make the walk endless), and a
/// file pattern that filters what is reported without stopping the descent.
/// </remarks>
internal sealed class FileWalker(IFileSystem files, IPath paths, IgnoreRuleProvider ignore)
{
    private readonly IFileSystem _files = files;
    private readonly IPath _paths = paths;
    private readonly IgnoreRuleProvider _ignore = ignore;

    /// <summary>
    /// Writes every accepted file to <paramref name="output"/> and completes it afterwards, whether
    /// the walk ended, failed or was cancelled. A failure is completed into the channel, so the
    /// workers stop instead of waiting for files that will never come.
    /// </summary>
    public async Task WalkAsync(SearchPlan plan, ChannelWriter<SearchCandidate> output, CancellationToken cancellationToken)
    {
        try
        {
            var rootRules = plan.RespectIgnoreFiles
                ? await _ignore.ForRootAsync(plan.Root, cancellationToken)
                : IgnoreRules.None;
            var pending = new Stack<(string Directory, IgnoreRules Rules)>();
            pending.Push((plan.Root, rootRules));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, rules) = pending.Pop();
                IReadOnlyList<FileSystemEntry> children;
                try
                {
                    children = await _files.ListEntriesAsync(directory, recursive: false, cancellationToken);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // One unreadable directory does not cost the caller every other file.
                    continue;
                }

                foreach (var entry in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = _paths.GetRelativePath(plan.Root, entry.Path);
                    if (entry.IsDirectory)
                    {
                        if (IsSkippable(plan, rules, entry, relative))
                        {
                            continue;
                        }

                        // A link is never followed: the walk would leave the searched tree, and an
                        // entry nobody asked to reach would be reported as if it were inside it.
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        var childRules = plan.RespectIgnoreFiles
                            ? rules.And(await _ignore.ForDirectoryAsync(entry.Path, cancellationToken))
                            : rules;
                        pending.Push((entry.Path, childRules));
                        continue;
                    }

                    if (IsSkippable(plan, rules, entry, relative) || !plan.Accepts(entry.Name, relative))
                    {
                        continue;
                    }

                    await output.WriteAsync(new SearchCandidate(entry.Path, relative), cancellationToken);
                }
            }

            output.TryComplete();
        }
        catch (Exception error)
        {
            // The channel is the only channel of failure the workers see, and completing it here is
            // also what frees them from a read that would otherwise wait forever.
            output.TryComplete(error);
            throw;
        }
    }

    /// <summary>
    /// Whether the entry is left out before it is considered at all: hidden, ignored, or excluded
    /// by a glob. The same answer applies to a directory and to everything below it.
    /// </summary>
    private bool IsSkippable(SearchPlan plan, IgnoreRules rules, FileSystemEntry entry, string relative) =>
        (!plan.IncludeHidden && entry.Name.StartsWith('.'))
        || plan.Exclude.IsMatch(entry.Name, relative)
        || rules.IsIgnored(plan.Root, entry.Path, entry.IsDirectory, _paths);
}
