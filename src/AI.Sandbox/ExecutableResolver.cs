namespace AI.Sandbox;

using System.ComponentModel;

/// <summary>
/// Finds the file a program name refers to before the program is confined: a full path is used as
/// is, a path with a separator is taken against the working directory, and a bare name is looked up
/// in the system directories (Windows) and the <c>PATH</c> the program will see. On Windows a name
/// without an extension is tried with each <c>PATHEXT</c> extension, so <c>npm</c> finds <c>npm.cmd</c>.
/// </summary>
internal static class ExecutableResolver
{
    /// <summary>The OS error a start reports for a missing program.</summary>
    private const int FileNotFound = 2;

    private static readonly string[] DefaultExtensions = [".COM", ".EXE", ".BAT", ".CMD"];

    /// <exception cref="Win32Exception">Nothing by that name exists, as a start without a sandbox would report it.</exception>
    public static string Resolve(string file, string workingDirectory, IDictionary<string, string?> environment)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new Win32Exception(FileNotFound);
        }

        if (Path.IsPathFullyQualified(file))
        {
            return Existing(file) ?? throw new Win32Exception(FileNotFound);
        }

        if (file.Contains(Path.DirectorySeparatorChar) || file.Contains(Path.AltDirectorySeparatorChar))
        {
            var directory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
            return Existing(Path.GetFullPath(file, directory)) ?? throw new Win32Exception(FileNotFound);
        }

        foreach (var directory in SearchDirectories(environment))
        {
            if (Existing(Path.Combine(directory, file)) is { } found)
            {
                return found;
            }
        }

        throw new Win32Exception(FileNotFound);
    }

    private static IEnumerable<string> SearchDirectories(IDictionary<string, string?> environment)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Environment.SystemDirectory;
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        }

        var path = environment.TryGetValue("PATH", out var value) && value is not null
            ? value
            : Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var unquoted = directory.Trim('"');
            if (Path.IsPathFullyQualified(unquoted))
            {
                yield return unquoted;
            }
        }
    }

    /// <summary>The program at <paramref name="candidate"/>, trying the Windows extensions when it names none.</summary>
    private static string? Existing(string candidate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return File.Exists(candidate) && IsExecutable(candidate) ? candidate : null;
        }

        if (Path.HasExtension(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? DefaultExtensions;
        return extensions.Select(extension => candidate + extension).FirstOrDefault(File.Exists);
    }

    private static bool IsExecutable(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(file) & execute) != 0;
    }
}
