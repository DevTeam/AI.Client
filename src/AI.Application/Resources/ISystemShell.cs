namespace AI.Application.Resources;

/// <summary>Hands a path on this machine to the operating system's default action for it.</summary>
public interface ISystemShell
{
    /// <summary>
    /// Opens a directory in the file manager and a document in its default application. A file the
    /// system would run instead of open is only shown selected in its folder.
    /// </summary>
    void Open(string path, bool isDirectory);
}
