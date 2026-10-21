namespace AI.Sandbox;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

/// <summary>
/// The macOS sandbox profile (SBPL) <c>sandbox-exec</c> applies to a program and everything it
/// starts. Only the file system is restricted: everything else stays allowed, since the policy is
/// about which directories a program reaches. Later rules win in SBPL, so the profile denies file
/// access as a whole and then opens the policy's paths.
/// </summary>
internal static partial class SeatbeltProfile
{
    public static string Build(SandboxPolicy policy)
    {
        var profile = new StringBuilder()
            .AppendLine("(version 1)")
            .AppendLine("(allow default)")
            .AppendLine("(deny file-read* file-write*)")
            // Looking a path up reads the metadata of every directory on the way; contents stay closed.
            .AppendLine("(allow file-read-metadata)")
            .AppendLine("(allow file-read* (literal \"/\"))");
        foreach (var entry in policy.Paths)
        {
            if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path))
            {
                continue;
            }

            var operations = entry.Access == SandboxAccess.ReadWrite ? "file-read* file-write*" : "file-read*";
            profile.Append("(allow ").Append(operations).Append(" (subpath ").Append(Quote(RealPath(entry.Path))).AppendLine("))");
        }

        if (!policy.AllowNetwork)
        {
            profile.AppendLine("(deny network*)");
        }

        return profile.ToString();
    }

    /// <summary>An SBPL string literal.</summary>
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// The path with every link resolved: the sandbox matches the path a file is really at, so a
    /// rule for <c>/tmp</c> would never match what lives in <c>/private/tmp</c>.
    /// </summary>
    private static string RealPath(string path)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return path;
        }

        return Resolve(path) ?? path;
    }

    [UnsupportedOSPlatform("windows")]
    private static string? Resolve(string path)
    {
        var resolved = realpath(path, 0);
        if (resolved == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            free(resolved);
        }
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint realpath(string path, nint resolved);

    [LibraryImport("libc")]
    private static partial void free(nint pointer);
}
