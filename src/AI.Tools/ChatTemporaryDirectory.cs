namespace AI.Infrastructure.Tools;

using System.Security.Cryptography;
using System.Text;
using AI.Contracts.FileSystem;
using Application.Tools;
using Microsoft.Extensions.Logging;

/// <summary>Creates chat scratch space below the current user's OS temporary directory.</summary>
public sealed partial class ChatTemporaryDirectory(
    ILogger<ChatTemporaryDirectory> logger,
    IFileSystem files,
    IPath paths) : IChatTemporaryDirectory
{
    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Built on where the OS temporary directory really is: on macOS <c>/var/folders</c> is a link to
    /// <c>/private/var/folders</c>, and a root spelled through it failed the link checks below on
    /// every Mac. Resolving the temporary directory itself keeps those checks for what they guard,
    /// links planted inside the scratch space.
    /// </summary>
    private readonly Lazy<string> _rootPath = new(() => Path.Combine(
        files.ResolveLinkTargetAsync(Path.GetTempPath(), CancellationToken.None).GetAwaiter().GetResult(),
        "AI.Client-" + UserKey()));

    private string Root => _rootPath.Value;

    public string GetOrCreate(Guid projectId, Guid chatId)
    {
        if (projectId == Guid.Empty || chatId == Guid.Empty)
            throw new ArgumentException("A project and chat are required for temporary storage.");

        var project = ProjectPath(projectId);
        var chat = ChatRoot(projectId, chatId);
        EnsurePrivateDirectory(Root);
        EnsurePrivateDirectory(project);
        EnsurePrivateDirectory(chat);
        return chat;
    }

    public string ChatRoot(Guid projectId, Guid chatId)
    {
        if (projectId == Guid.Empty || chatId == Guid.Empty)
            throw new ArgumentException("A project and chat are required for temporary storage.");
        return Path.Combine(ProjectPath(projectId), chatId.ToString("N"));
    }

    public void DeleteChat(Guid projectId, Guid chatId)
    {
        if (projectId == Guid.Empty || chatId == Guid.Empty) return;
        TryDelete(Path.Combine(ProjectPath(projectId), chatId.ToString("N")));
    }

    public void DeleteProject(Guid projectId)
    {
        if (projectId == Guid.Empty) return;
        TryDelete(ProjectPath(projectId));
    }

    public string ProjectRoot(Guid projectId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project is required for temporary storage.");
        return ProjectPath(projectId);
    }

    private string ProjectPath(Guid projectId) => Path.Combine(Root, projectId.ToString("N"));

    private static string UserKey()
    {
        var identity = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\0"
                       + Environment.UserDomainName + "\0" + Environment.UserName;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    private void EnsurePrivateDirectory(string path)
    {
        if (files.DirectoryExistsAsync(path, CancellationToken.None).GetAwaiter().GetResult() && IsLink(path))
            throw new IOException("The chat temporary directory cannot be a link.");

        if (OperatingSystem.IsWindows())
        {
            // The single-argument form of the platform's CreateDirectory, behind the contract.
            files.CreateDirectoryAsync(path, ownerOnly: false, CancellationToken.None).GetAwaiter().GetResult();
        }
        else
        {
            // A private POSIX mode is the documented exception the frozen contract has no member for:
            // the directory is created with it here and asserted again below.
            Directory.CreateDirectory(path, PrivateMode);
        }

        if (IsLink(path))
            throw new IOException("The chat temporary directory cannot be a link.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateMode);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (!files.DirectoryExistsAsync(Root, CancellationToken.None).GetAwaiter().GetResult()) return;
            if (IsLink(Root))
                throw new IOException("The chat temporary root is a link.");
            if (files.DirectoryExistsAsync(path, CancellationToken.None).GetAwaiter().GetResult()) DeleteTree(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LogCleanupFailure(error, path);
        }
    }

    /// <summary>
    /// Removes a tree without ever following a link. The walk is kept instead of a recursive delete
    /// because refusing to follow a reparse point is the point of it: a link inside the scratch
    /// space is removed as a link, never as the directory it points at.
    /// </summary>
    private void DeleteTree(string directory)
    {
        foreach (var entry in files.ListEntriesAsync(directory, recursive: false, CancellationToken.None)
                     .GetAwaiter().GetResult())
        {
            if (entry.IsDirectory && !IsLink(entry.Path)) DeleteTree(entry.Path);
            else if (entry.IsDirectory)
                files.DeleteDirectoryAsync(entry.Path, recursive: false, CancellationToken.None).GetAwaiter().GetResult();
            else files.DeleteFileAsync(entry.Path, CancellationToken.None).GetAwaiter().GetResult();
        }
        files.DeleteDirectoryAsync(directory, recursive: false, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Whether the path is a link. The contract reports a link's target attributes and never a link
    /// flag, so a path whose canonical resolution differs from its own spelling is one that leads
    /// somewhere other than where it stands — exactly what these guards have to refuse.
    /// </summary>
    private bool IsLink(string path)
    {
        try
        {
            var full = paths.TrimEndingDirectorySeparator(paths.GetFullPath(path));
            var resolved = paths.TrimEndingDirectorySeparator(
                files.ResolveLinkTargetAsync(path, CancellationToken.None).GetAwaiter().GetResult());
            return !string.Equals(resolved, full, paths.Comparison);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException)
        {
            // A path that cannot be resolved is treated as one this walk must not follow.
            return true;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Could not remove chat temporary directory {Path}")]
    private partial void LogCleanupFailure(Exception error, string path);
}
