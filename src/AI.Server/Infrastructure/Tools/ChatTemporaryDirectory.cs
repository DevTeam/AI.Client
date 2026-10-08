namespace AI.Infrastructure.Tools;

using System.Security.Cryptography;
using System.Text;
using Application.Tools;
using Microsoft.Extensions.Logging;

/// <summary>Creates chat scratch space below the current user's OS temporary directory.</summary>
public sealed partial class ChatTemporaryDirectory(ILogger<ChatTemporaryDirectory> logger) : IChatTemporaryDirectory
{
    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "AI.Client-" + UserKey());

    public string GetOrCreate(Guid projectId, Guid chatId)
    {
        if (projectId == Guid.Empty || chatId == Guid.Empty)
            throw new ArgumentException("A project and chat are required for temporary storage.");

        var project = ProjectPath(projectId);
        var chat = Path.Combine(project, chatId.ToString("N"));
        EnsurePrivateDirectory(_root);
        EnsurePrivateDirectory(project);
        EnsurePrivateDirectory(chat);
        return chat;
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

    private string ProjectPath(Guid projectId) => Path.Combine(_root, projectId.ToString("N"));

    private static string UserKey()
    {
        var identity = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\0"
                       + Environment.UserDomainName + "\0" + Environment.UserName;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    private static void EnsurePrivateDirectory(string path)
    {
        if (Directory.Exists(path) && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The chat temporary directory cannot be a link.");

        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, PrivateMode);

        if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The chat temporary directory cannot be a link.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateMode);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            if (new DirectoryInfo(_root).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("The chat temporary root is a link.");
            if (Directory.Exists(path)) DeleteTree(new DirectoryInfo(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LogCleanupFailure(error, path);
        }
    }

    private static void DeleteTree(DirectoryInfo directory)
    {
        if (!directory.Exists) return;
        if (!directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child) DeleteTree(child);
                else entry.Delete();
            }
        directory.Delete();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Could not remove chat temporary directory {Path}")]
    private partial void LogCleanupFailure(Exception error, string path);
}
