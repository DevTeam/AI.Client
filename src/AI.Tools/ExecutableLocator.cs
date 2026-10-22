namespace AI.Infrastructure.Tools;

using AI.Application.Tools.Commands;
using AI.Contracts.FileSystem;

/// <summary>
/// Finds programs the way Windows' CreateProcess and a Unix exec do for <c>process_run</c>: a bare
/// name through PATH, with <c>.exe</c> added on Windows when it has no extension. The call then runs
/// the file found here, so what a rule was checked against is what starts.
/// </summary>
public sealed class ExecutableLocator(IFileSystem files, IPath paths) : IExecutableLocator
{
    /// <summary>Where PATH is read from; a test hands in its own.</summary>
    public Func<string?> SearchPath { get; init; } = () => Environment.GetEnvironmentVariable("PATH");

    public async Task<string?> LocateAsync(string executable, string? workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        try
        {
            if (paths.IsFullyQualified(executable)) return await ExistingAsync(paths.GetFullPath(executable), cancellationToken);
            if (executable.IndexOfAny([paths.DirectorySeparator, paths.AltDirectorySeparator]) >= 0)
                return workingDirectory is null
                    ? null
                    : await ExistingAsync(paths.GetFullPath(executable, workingDirectory), cancellationToken);
            var name = !paths.IsCaseSensitive && paths.GetExtension(executable).Length == 0 ? executable + ".exe" : executable;
            foreach (var directory in (SearchPath() ?? "").Split(paths.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = directory.Trim('"');
                // A relative PATH entry points wherever the server happens to stand; it finds nothing a rule can name.
                if (!paths.IsFullyQualified(root)) continue;
                if (await ExistingAsync(paths.Combine(paths.GetFullPath(root), name), cancellationToken) is { } found) return found;
            }
            return null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private async Task<string?> ExistingAsync(string path, CancellationToken cancellationToken) =>
        await files.FileExistsAsync(path, cancellationToken) ? path : null;
}
