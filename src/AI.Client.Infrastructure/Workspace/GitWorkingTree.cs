namespace AI.Client.Infrastructure.Workspace;

using System.Diagnostics;

/// <summary>
/// Read-only questions to a Git working tree, used to notice files an external process wrote that
/// the Host never saw go past.
/// </summary>
/// <remarks>
/// Every invocation passes <c>--no-optional-locks</c> and asks only for status. Git is not allowed
/// to take the index lock, refresh the index, or write anything: this is an observer of the user's
/// repository, never a participant in it. Nothing here stages, resets, or stashes.
///
/// Git is also not required. A workspace that is not a repository, a machine without Git, a repo
/// too slow to answer — all of them simply yield no information, and the change set stays as it
/// was rather than failing.
/// </remarks>
public static class GitWorkingTree
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Absolute paths Git considers changed relative to HEAD inside <paramref name="root"/>,
    /// including untracked files. Null when the question could not be answered at all, which is
    /// different from "nothing changed" and must not be read as such.
    /// </summary>
    public static IReadOnlySet<string>? DirtyPaths(string root)
    {
        // -uall lists untracked files individually rather than collapsing them into a directory,
        // which is what makes a newly created file visible here.
        if (Run(root, "--no-optional-locks", "status", "--porcelain=v1", "-uall", "-z") is not { } output) return null;

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // Porcelain v1: two status characters, a space, then the path.
            if (entry.Length < 4) continue;
            var path = entry[3..];
            // A rename record is followed by its source path as a separate NUL-terminated field;
            // both ends are interesting, and the source arrives on the next iteration.
            try
            {
                paths.Add(Path.GetFullPath(Path.Combine(root, path)));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A path this platform cannot represent is simply not reported.
            }
        }
        return paths;
    }

    private static string? Run(string workingDirectory, params string[] arguments)
    {
        if (!Directory.Exists(workingDirectory)) return null;
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            // A non-zero exit is the normal answer for "not a repository"; it is not an error here.
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException
                                          or IOException or UnauthorizedAccessException)
        {
            // Git missing or unusable: the caller carries on without it.
            return null;
        }
    }
}
