namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class SearchFilesTool(IPathGuard guard) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        SearchAsync,
        new McpServerToolCreateOptions
        {
            Description = "Search a directory tree for entries matching a glob pattern. '*' matches within one segment, '**' crosses "
                          + "segments and '?' matches one character. A pattern without a separator matches the entry name, otherwise the "
                          + $"path relative to the search root. At most {FileLimits.SearchMatches} matches are returned. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "search_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SearchFilesResult))]
    private Task<CallToolResult> SearchAsync(
        [Description("Absolute path of the directory to search.")] [MaxLength(4096)] string path,
        [Description("Glob pattern to match, for example '*.cs' or 'src/**/*.razor'.")] [MaxLength(512)] string pattern,
        [Description("Glob patterns to skip; a matching directory is not descended into.")] [MaxLength(64)] string[]? excludePatterns = null,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return Task.FromResult(ToolReply.Of(new SearchFilesResult(path, [], false, error.Message), true));
        }

        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(ToolReply.Of(new SearchFilesResult(resolved, [], false, "Directory does not exist."), true));
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
            return Task.FromResult(ToolReply.Of(new SearchFilesResult(resolved, [], false, error.Message), true));
        }

        var matches = new List<string>();
        var examined = 0;
        var truncated = false;
        var queue = new Queue<string>();
        queue.Enqueue(resolved);
        while (queue.Count > 0 && !truncated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(current).EnumerateFileSystemInfos();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var info in children)
            {
                if (++examined > FileLimits.SearchExamined || matches.Count == FileLimits.SearchMatches)
                {
                    truncated = true;
                    break;
                }

                var relative = Path.GetRelativePath(resolved, info.FullName);
                if (excludes.Any(exclude => exclude.IsMatch(info.Name, relative)))
                {
                    continue;
                }

                if (include.IsMatch(info.Name, relative))
                {
                    matches.Add(info.FullName);
                }

                if ((info.Attributes & FileAttributes.Directory) != 0 && info.LinkTarget is null)
                {
                    queue.Enqueue(info.FullName);
                }
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(ToolReply.Of(new SearchFilesResult(resolved, matches.ToArray(), truncated, null)));
    }
}
