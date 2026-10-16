namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AI.Contracts.FileSystem;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class SearchFilesTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files, IPath paths) : IToolFactory
{
    private readonly IPath _paths = paths;

    // Approximate JSON overhead per match beyond its own path string — quotes and a comma in the
    // `matches` string array, inflated to account for the result being serialized a second time
    // when it's stored as the tool's chat message (see ResultBudget).
    private const int MatchOverheadCharacters = 16;

    public McpServerTool Create() => McpServerTool.Create(
        SearchAsync,
        new McpServerToolCreateOptions
        {
            Description = "Search a directory tree for entries matching a glob pattern. '*' matches within one segment, '**' crosses "
                          + "segments and '?' matches one character. A pattern without a separator matches the entry name, otherwise the "
                          + "path relative to the search root. Matches are paths relative to `path`. The result is capped both by match "
                          + $"count (at most {FileLimits.SearchMatches}) and by total size; either cap sets `truncated: true`. By default, "
                          + "version control and build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, .idea, .vscode) "
                          + "and whatever the Git repository's ignore files ignore are skipped; pass excludeDefaults: false to search them too. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "search_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SearchFilesResult))]
    private async Task<CallToolResult> SearchAsync(
        [Description("Absolute path of the directory to search.")] [MaxLength(4096)] string path,
        [Description("Glob pattern to match, for example '*.cs' or 'src/**/*.razor'.")] [MaxLength(512)] string pattern,
        [Description("Glob patterns to skip; a matching directory is not descended into.")] [MaxLength(64)] string[]? excludePatterns = null,
        [Description("Skip version control and build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, .idea, .vscode) and Git-ignored entries by default.")]
        bool excludeDefaults = true,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return reply.Reply(new SearchFilesResult(path, [], false, error.Message), true);
        }

        if (!await files.DirectoryExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(new SearchFilesResult(resolved, [], false, "Directory does not exist."), true);
        }

        GlobPattern include;
        GlobPattern[] excludes;
        try
        {
            include = GlobPattern.Parse(pattern);
            excludes = (excludePatterns ?? []).Select(GlobPattern.Parse).ToArray();
        }
        catch (ArgumentException error)
        {
            return reply.Reply(new SearchFilesResult(resolved, [], false, error.Message), true);
        }

        var matches = new List<string>();
        var budget = new ResultBudget(FileLimits.SearchCharacters);
        var examined = 0;
        var truncated = false;
        var queue = new Queue<(string Path, IgnoreRules? Parent)>();
        var rootRules = excludeDefaults ? await IgnoreRules.ForRootAsync(files, _paths, resolved, cancellationToken) : IgnoreRules.None;
        queue.Enqueue((resolved, null));
        while (queue.Count > 0 && !truncated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, parent) = queue.Dequeue();
            IReadOnlyList<FileSystemEntry> children;
            try
            {
                children = await files.ListEntriesAsync(current, recursive: false, cancellationToken);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var rules = parent is null ? rootRules : await parent.EnterAsync(files, _paths, current, children, cancellationToken);

            foreach (var info in children)
            {
                if (++examined > FileLimits.SearchExamined || matches.Count == FileLimits.SearchMatches)
                {
                    truncated = true;
                    break;
                }

                var directory = info.IsDirectory;
                if (directory && excludeDefaults && FileLimits.DefaultExcludedNames.Contains(info.Name))
                {
                    continue;
                }

                var relative = _paths.GetRelativePath(resolved, info.Path);
                if (excludes.Any(exclude => exclude.IsMatch(info.Name, relative)) || rules.IsIgnored(_paths, info.Path, directory))
                {
                    continue;
                }

                if (include.IsMatch(info.Name, relative))
                {
                    // Relative to the root the result names once: the same absolute prefix on every
                    // match was most of a large result, and all of it went into the model's context.
                    if (!budget.TryReserve(relative.Length + MatchOverheadCharacters))
                    {
                        truncated = true;
                        break;
                    }

                    matches.Add(relative);
                }

                // A link is never followed: descending through one leads back up the tree, and a
                // search that walks out of the granted root is a search that reports too much. A
                // reparse point that resolves to itself is not a link and is descended into.
                if (directory && !await LinkTargets.IsLinkAsync(files, _paths, info.Path, info.Attributes, cancellationToken))
                {
                    queue.Enqueue((info.Path, rules));
                }
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return reply.Reply(new SearchFilesResult(resolved, matches.ToArray(), truncated, null));
    }
}
