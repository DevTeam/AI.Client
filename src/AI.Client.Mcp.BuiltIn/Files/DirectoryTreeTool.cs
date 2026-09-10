namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class DirectoryTreeTool(IPathGuard guard) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        TreeAsync,
        new McpServerToolCreateOptions
        {
            Description = "Walk a directory tree and return a flat list of entries with their path relative to the root, kind and depth. "
                          + $"At most {FileLimits.TreeEntries} entries and {FileLimits.TreeDepth} levels are returned; directory links are "
                          + "listed but not followed. The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "directory_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DirectoryTreeResult))]
    private Task<CallToolResult> TreeAsync(
        [Description("Absolute path of the directory to walk.")] [MaxLength(4096)] string path,
        [Description("Maximum depth to descend, where 1 lists only immediate children.")] [Range(1, FileLimits.TreeDepth)] int maxDepth = FileLimits.TreeDepth,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return Task.FromResult(ToolReply.Of(new DirectoryTreeResult(path, [], false, error.Message), true));
        }

        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(ToolReply.Of(new DirectoryTreeResult(resolved, [], false, "Directory does not exist."), true));
        }

        var entries = new List<TreeEntry>();
        var truncated = Walk(resolved, resolved, 1, Math.Min(maxDepth, FileLimits.TreeDepth), entries, cancellationToken);
        return Task.FromResult(ToolReply.Of(new DirectoryTreeResult(resolved, entries.ToArray(), truncated, null)));
    }

    private static bool Walk(string root, string current, int depth, int maxDepth, List<TreeEntry> entries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemInfo[] children;
        try
        {
            children = new DirectoryInfo(current).GetFileSystemInfos();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        Array.Sort(children, (left, right) => string.CompareOrdinal(left.Name, right.Name));
        var truncated = false;
        foreach (var info in children)
        {
            if (entries.Count == FileLimits.TreeEntries)
            {
                return true;
            }

            var directory = (info.Attributes & FileAttributes.Directory) != 0;
            entries.Add(new TreeEntry(Path.GetRelativePath(root, info.FullName), directory ? "directory" : "file", depth));
            if (!directory || info.LinkTarget is not null)
            {
                continue;
            }

            if (depth == maxDepth)
            {
                truncated = true;
                continue;
            }

            truncated |= Walk(root, info.FullName, depth + 1, maxDepth, entries, cancellationToken);
        }

        return truncated;
    }
}
