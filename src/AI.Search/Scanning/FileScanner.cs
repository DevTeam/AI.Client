namespace AI.Search.Scanning;

using System.Buffers;
using AI.Contracts.FileSystem;
using AI.Search.Matchers;
using AI.Search.Walking;

/// <summary>What scanning one file came to.</summary>
internal enum FileOutcome
{
    /// <summary>The file was read and holds at least one match.</summary>
    Matched,

    /// <summary>The file was read and holds no match.</summary>
    NoMatch,

    /// <summary>The file was left alone: binary, too large, or unreadable.</summary>
    Skipped
}

/// <summary>One scanned file: what it came to, what it holds, and how many bytes were read.</summary>
internal readonly record struct FileScanResult(FileOutcome Outcome, FoundFile? File, long Bytes);

/// <summary>
/// Reads one file and scans it. Every file is opened through the file-system contract with sharing
/// that lets somebody else save it meanwhile, and with a sequential hint: this read walks the file
/// once and never comes back.
/// </summary>
internal sealed class FileScanner(IFileSystem files)
{
    /// <summary>Reads and scans the file with the plan's limits.</summary>
    /// <param name="path">The absolute path of the file to read.</param>
    /// <param name="relativePath">The path reported to the caller.</param>
    /// <param name="plan">The limits of this search.</param>
    /// <param name="scanner">The compiled pattern; one instance per worker.</param>
    /// <param name="limit">Matches to report from this file; 0 means no limit.</param>
    /// <param name="matches">Scratch space for the reported matches.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<FileScanResult> ScanAsync(
        string path,
        string relativePath,
        SearchPlan plan,
        IContentScanner scanner,
        int limit,
        List<SearchMatch> matches,
        CancellationToken cancellationToken)
    {
        // No stream buffer: the file is read into a pooled buffer of its own size below, and a
        // buffered stream would only copy it a second time through a fresh 64 KB block per file.
        var options = new FileReadOptions(
            Share: FileShare.ReadWrite | FileShare.Delete,
            BufferSize: 0,
            Options: FileOptions.SequentialScan | FileOptions.Asynchronous);
        Stream? stream;
        try
        {
            stream = await files.OpenReadAsync(path, options, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new FileScanResult(FileOutcome.Skipped, null, 0);
        }

        if (stream is null)
        {
            return new FileScanResult(FileOutcome.Skipped, null, 0);
        }

        await using (stream)
        {
            try
            {
                var content = await ReadAsync(stream, plan, cancellationToken);
                if (content is null)
                {
                    return new FileScanResult(FileOutcome.Skipped, null, 0);
                }

                var (buffer, length) = content.Value;
                try
                {
                    var firstOnly = plan.Output == MatchOutput.Files;
                    // A file listing stops at the first match of a file and carries no line text: the
                    // caller asked which files match, not what is in them.
                    var count = scanner.Scan(buffer, length, firstOnly ? 1 : limit, firstOnly, matches);
                    if (count == 0)
                    {
                        return new FileScanResult(FileOutcome.NoMatch, null, length);
                    }

                    var found = new FoundFile(relativePath, count, firstOnly ? [] : matches.ToArray());
                    return new FileScanResult(FileOutcome.Matched, found, length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A file that becomes unreadable after it was listed is skipped like one that never
                // opened; a search of a busy tree must not fail over a single entry.
                return new FileScanResult(FileOutcome.Skipped, null, 0);
            }
        }
    }

    /// <summary>
    /// Reads the file into a buffer of exactly its size, which is what the scan wants anyway, or
    /// into a pooled buffer that grows while the length is unknown. The file is read whole before it
    /// is scanned because the scan needs a complete line boundary to report columns consistently.
    /// </summary>
    /// <returns>The buffer and its length, or null when the file is skipped.</returns>
    private static async Task<(byte[] Buffer, int Length)?> ReadAsync(Stream stream, SearchPlan plan, CancellationToken cancellationToken)
    {
        var limit = (int)Math.Min(plan.MaxFileBytes, Array.MaxLength);
        var capacity = 65536;
        if (stream.CanSeek)
        {
            var length = stream.Length;
            if (length > plan.MaxFileBytes)
            {
                // ripgrep's own limit starts at 16 MB and covers the size it is asked about here.
                return null;
            }

            // One byte more than the length tells "exactly at the limit" from "larger".
            capacity = (int)Math.Max(length, 1);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(capacity, limit + 1));
        var count = 0;
        try
        {
            while (true)
            {
                if (count == buffer.Length)
                {
                    if (count > limit / 2)
                    {
                        // A file whose length could not be read up front turned out to be too large.
                        ArrayPool<byte>.Shared.Return(buffer);
                        return null;
                    }

                    var larger = ArrayPool<byte>.Shared.Rent(Math.Min(count * 2, limit + 1));
                    buffer.AsSpan(0, count).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var room = Math.Min(buffer.Length - count, Math.Max((limit + 1) - count, 1));
                var read = await stream.ReadAsync(buffer.AsMemory(count, room), cancellationToken);
                count += read;
                if (plan.SkipBinary && IsBinary(buffer.AsSpan(0, Math.Min(count, LineMatchers.BinaryProbeBytes))))
                {
                    // Decided on the first probe, so a binary file costs one read rather than a full pass.
                    ArrayPool<byte>.Shared.Return(buffer);
                    return null;
                }

                if (count > limit)
                {
                    // The limit is a limit: a file that crosses it is skipped rather than searched in part.
                    ArrayPool<byte>.Shared.Return(buffer);
                    return null;
                }

                if (read == 0)
                {
                    return (buffer, count);
                }
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Whether the content is binary: a NUL byte in its head, which is ripgrep's rule. A text file
    /// in UTF-16 has NUL bytes too and is skipped, exactly as ripgrep skips it.
    /// </summary>
    private static bool IsBinary(ReadOnlySpan<byte> probe) => probe.Contains((byte)0);
}
