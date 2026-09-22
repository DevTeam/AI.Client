namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ListDirectoryTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    // Approximate JSON overhead per entry beyond its own name — quotes/keys/commas for
    // `{"name":"...","kind":"file","size":123,"modifiedAt":"..."}`, inflated to account for the
    // result being serialized a second time when it's stored as the tool's chat message (see
    // ResultBudget).
    private const int EntryOverheadCharacters = 96;

    public McpServerTool Create() => McpServerTool.Create(
        ListAsync,
        new McpServerToolCreateOptions
        {
            Description = "List the immediate entries of a directory with their kind, size and modification time. "
                          + $"At most {FileLimits.DirectoryEntries} entries are returned, and the result is also capped by total size; "
                          + "either cap sets `truncated: true`. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "list_directory", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DirectoryListResult))]
    private Task<CallToolResult> ListAsync(
        [Description("Absolute path of the directory to list.")] [MaxLength(4096)] string path,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return Task.FromResult(reply.Reply(new DirectoryListResult(path, [], false, error.Message), true));
        }

        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(reply.Reply(new DirectoryListResult(resolved, [], false, "Directory does not exist."), true));
        }

        try
        {
            var entries = new List<DirectoryEntryInfo>();
            var budget = new ResultBudget(FileLimits.DirectoryCharacters);
            var truncated = false;
            foreach (var info in new DirectoryInfo(resolved).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count == FileLimits.DirectoryEntries || !budget.TryReserve(info.Name.Length + EntryOverheadCharacters))
                {
                    truncated = true;
                    break;
                }

                var directory = (info.Attributes & FileAttributes.Directory) != 0;
                entries.Add(new DirectoryEntryInfo(
                    info.Name,
                    directory ? "directory" : "file",
                    directory ? null : (info as FileInfo)?.Length,
                    info.LastWriteTimeUtc));
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return Task.FromResult(reply.Reply(new DirectoryListResult(resolved, entries.ToArray(), truncated, null)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(reply.Reply(new DirectoryListResult(resolved, [], false, error.Message), true));
        }
    }
}
