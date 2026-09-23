namespace Build.Targets;

/// <summary>
/// Publishes the desktop app self-contained for one platform: the target machine needs no .NET,
/// only the system web view (WebView2 on Windows, WebKitGTK or WPE WebKit on Linux).
/// </summary>
internal sealed class PublishDesktopTarget(IProcessRunner processRunner) : IPublishDesktopTarget
{
    public Task<int> RunAsync(string runtime, string outputDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            $"Publish desktop ({runtime})",
            "dotnet",
            ["publish", "src/AI.Client.Desktop/AI.Client.Desktop.csproj", "--nologo", "-c", "Release",
                "-r", runtime, "--self-contained", "--output", outputDirectory],
            cancellationToken);
}
