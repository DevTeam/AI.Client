namespace AI.Infrastructure.Workspace;

using System.Text;
using AI.Application.Instructions;

/// <summary>
/// Reads AGENTS.md and CLAUDE.md at the root of each granted directory. Only the roots are looked
/// at: a nested file belongs to a subtree the model reads on its own when it works there.
/// </summary>
public sealed class WorkspaceInstructionFileReader : IInstructionFileReader
{
    public static readonly IReadOnlyList<string> FileNames = ["AGENTS.md", "CLAUDE.md"];

    /// <summary>More than any layer budget holds; the budget, not this limit, normally cuts a long file.</summary>
    private const int ReadLimitBytes = 64 * 1024;

    public async Task<IReadOnlyList<InstructionFile>> ReadAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new List<InstructionFile>();
        foreach (var root in roots.Distinct(comparer))
        foreach (var name in FileNames)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[Math.Min(stream.Length, ReadLimitBytes)];
                var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
                var text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('﻿').Trim();
                if (text.Length > 0) result.Add(new InstructionFile(path, text, stream.Length > ReadLimitBytes));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be read now is simply absent from this run; the next run tries again.
            }
        }
        return result;
    }
}
