namespace AI.Infrastructure.Storage;

public sealed class PhysicalTextFileSystem : ITextFileSystem
{
    /// <summary>
    /// Wait up to one second for a sharing violation caused by another process temporarily opening
    /// a stored document exclusively. A missing document is reported immediately.
    /// </summary>
    private const int MaxReadAttempts = 41;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(25);

    public Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories) : []);
    }
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(path));

    public Task<IReadOnlyList<string>> ListFilesAsync(
        string directoryPath,
        string searchPattern,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            Directory.Exists(directoryPath)
                ? Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly)
                : []);

    /// <summary>
    /// Reads a stored document while a save of it may be landing. Sharing must include delete:
    /// a save finishes by renaming a temporary file over this one, and Windows refuses to replace
    /// a file that somebody holds open without it. The saver then fails — with an access denial
    /// naming a directory whose permissions were never the problem — because a reader happened to
    /// be looking. Each file is written whole, so a reader sees either the old document or the new
    /// one, never half of either.
    /// </summary>
    public async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    Options = FileOptions.Asynchronous,
                });
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException error) when (IsSharingViolation(error) && attempt < MaxReadAttempts)
            {
                await Task.Delay(ReadRetryDelay, cancellationToken);
            }
            catch (IOException error)
            {
                throw new IOException($"'{path}' could not be read: {error.Message}", error);
            }
            catch (UnauthorizedAccessException error)
            {
                throw new UnauthorizedAccessException($"'{path}' could not be read: {error.Message}", error);
            }
        }
    }

    private static bool IsSharingViolation(IOException error) =>
        OperatingSystem.IsWindows() && (error.HResult & 0xFFFF) is 32 or 33;

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Storage path must include a directory.");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    public async Task AppendTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Storage path must include a directory.");
        try
        {
            Directory.CreateDirectory(directory);
            await File.AppendAllTextAsync(path, content, cancellationToken);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    /// <summary>
    /// Completes a save by putting the temporary file in the stored one's place.
    /// </summary>
    /// <remarks>
    /// Replacing an existing document goes through <see cref="File.Replace(string, string, string?)"/>
    /// rather than a move. Windows refuses to rename over a file that anybody holds open — sharing
    /// the read does not help, the rename is denied all the same — and the save then failed with an
    /// access denial while nothing was wrong with the directory or its permissions. Replacing is the
    /// operation the platform provides for exactly this: it swaps the contents under readers that
    /// already have the file open, and keeps the destination's own attributes.
    /// </remarks>
    public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken)
    {
        try
        {
            if (overwrite && File.Exists(destinationPath))
                File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);
            else File.Move(sourcePath, destinationPath, overwrite);
        }
        catch (FileNotFoundException) when (overwrite)
        {
            // The document was deleted between the test and the replace. There is nothing to replace
            // any more, and putting the new one in its place is what was being asked for regardless.
            Retry(() => File.Move(sourcePath, destinationPath, true), destinationPath);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, destinationPath);
        }

        return Task.CompletedTask;
    }

    private static void Retry(Action action, string path)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // A document in a missing directory is already absent.
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Puts the path back into the failure. "Access to the path is denied" names no path, so what
    /// reached the user was a stack frame and a verdict with nothing to act on — the one fact that
    /// turns it into a fixable problem, which file, is the fact the framework leaves out. The type
    /// is preserved because callers classify storage failures by it.
    /// </summary>
    private static Exception Named(Exception error, string path) => error switch
    {
        UnauthorizedAccessException => new UnauthorizedAccessException(
            $"Access to '{path}' is denied. Another program may be holding it open, or it is read-only, or the account "
            + "running this application may not write to that directory.", error),
        _ => new IOException($"'{path}' could not be written: {error.Message}", error),
    };
}
