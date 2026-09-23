namespace AI.Client.Desktop;

/// <summary>How the server came up, which is all the window needs to know.</summary>
/// <param name="Address">Where the server serves the UI, or null when it could not start.</param>
/// <param name="Error">Why it could not start.</param>
/// <param name="DataDirectory">The data directory, which also keeps the web view's own profile.</param>
/// <param name="DevTools">Whether the web view's developer tools are enabled.</param>
internal sealed record DesktopStart(Uri? Address, string? Error, string DataDirectory, bool DevTools);
