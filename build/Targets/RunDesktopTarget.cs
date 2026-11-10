namespace Build.Targets;

using System.Runtime.InteropServices;

internal sealed class RunDesktopTarget(IProcessRunner processes, IBuildPaths paths,
    IPrepareLocalAiTarget prepare) : IRunDesktopTarget
{
    public async Task<int> RunAsync(string? dataDirectory, bool localAi, CancellationToken cancellationToken)
    {
        var package = localAi ? await prepare.RunAsync(RuntimeInformation.RuntimeIdentifier, cancellationToken) : null;
        var output = Path.Combine(paths.SolutionDirectory, "artifacts", "run-desktop");
        var result = await processes.RunAsync("Publish desktop for running", "dotnet",
            ["publish", "src/AI.Desktop/AI.Desktop.csproj", "--nologo", "-c", "Debug", "--output", output], cancellationToken);
        if (result != 0) return result;
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            arguments.AddRange(["--data-dir", Path.GetFullPath(dataDirectory, paths.SolutionDirectory)]);
        return await processes.RunAsync("Run desktop", Path.Combine(output,
                OperatingSystem.IsWindows() ? "AI.Desktop.exe" : "AI.Desktop"), arguments, cancellationToken,
            package is null ? null : new Dictionary<string, string> { ["AI_LOCAL_MODEL_DIRECTORY"] = package });
    }
}
