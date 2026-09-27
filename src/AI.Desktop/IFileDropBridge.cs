namespace AI.Desktop;

using Avalonia.Platform;

/// <summary>
/// Hands the page's dropped files back to the host as local paths. A native web view takes the OS
/// drop itself, so Avalonia's drag events never see it and the page's File objects carry no path.
/// </summary>
internal interface IFileDropBridge
{
    /// <summary>Starts listening on the engine behind <paramref name="webView"/>; repeated calls are no-ops.</summary>
    void Attach(IPlatformHandle? webView, Action<IReadOnlyList<string>> dropped);

    void Detach();
}
