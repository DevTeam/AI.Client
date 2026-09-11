namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class DirectoryTreeTool(IPathGuard guard) : IToolFactory
{
    // Approximate JSON overhead per entry beyond its own path string — quotes/keys/commas for
    // `{"path":"...","kind":"directory","depth":1}`, inflated to account for the result being
    // serialized a second time when it's stored as the tool's chat message (see ResultBudget).
    private const int EntryOverheadCharacters = 64;

    public McpServerTool Create() => McpServerTool.Create(
        TreeAsync,
        new McpServerToolCreateOptions
        {
            Description = "Walk a directory tree and return a flat list of entries with their path relative to the root, kind and depth. "
                          + $"At most {FileLimits.TreeEntries} entries and {FileLimits.TreeDepth} levels are returned, and the result is "
                          + "also capped by total size — either cap sets `truncated: true`. Directory links are listed but not followed. "
                          + "By default, version control and build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, "
                          + ".idea, .vscode) are skipped entirely at any depth; pass excludeDefaults: false to see them. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "directory_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DirectoryTreeResult))]
    private Task<CallToolResult> TreeAsync(
        [Description("Absolute path of the directory to walk.")] [MaxLength(4096)] string path,
        [Description("Maximum depth to descend, where 1 lists only immediate children.")] [Range(1, FileLimits.TreeDepth)] int maxDepth = FileLimits.TreeDepth,
        [Description("Skip version control and build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, .idea, .vscode) by default.")]
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
            return Task.FromResult(ToolReply.Of(new DirectoryTreeResult(path, [], false, error.Message), true));
        }

        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(ToolReply.Of(new DirectoryTreeResult(resolved, [], false, "Directory does not exist."), true));
        }

        var entries = new List<TreeEntry>();
        var budget = new ResultBudget(FileLimits.TreeCharacters);
        var truncated = Walk(resolved, resolved, 1, Math.Min(maxDepth, FileLimits.TreeDepth), excludeDefaults, entries, budget, cancellationToken);
        return Task.FromResult(ToolReply.Of(new DirectoryTreeResult(resolved, entries.ToArray(), truncated, null)));
    }

    private static bool Walk(
        string root,
        string current,
        int depth,
        int maxDepth,
        bool excludeDefaults,
        List<TreeEntry> entries,
        ResultBudget budget,
        CancellationToken cancellationToken)
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
            var directory = (info.Attributes & FileAttributes.Directory) != 0;
            if (directory && excludeDefaults && FileLimits.DefaultExcludedNames.Contains(info.Name))
            {
                continue;
            }

            if (entries.Count == FileLimits.TreeEntries)
            {
                return true;
            }

            var relativePath = Path.GetRelativePath(root, info.FullName);
            if (!budget.TryReserve(relativePath.Length + EntryOverheadCharacters))
            {
                return true;
            }

            entries.Add(new TreeEntry(relativePath, directory ? "directory" : "file", depth));
            if (!directory || info.LinkTarget is not null)
            {
                continue;
            }

            if (depth == maxDepth)
            {
                truncated = true;
                continue;
            }

            truncated |= Walk(root, info.FullName, depth + 1, maxDepth, excludeDefaults, entries, budget, cancellationToken);
        }

        return truncated;
    }
}
