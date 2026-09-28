namespace Build.Targets;

/// <summary>Builds both self-contained products and their native packages on the target OS runner.</summary>
internal sealed class PackageReleaseTarget(IProcessRunner processes, IBuildPaths paths) : IPackageReleaseTarget
{
    public async Task<int> RunAsync(string runtime, string version, CancellationToken cancellationToken)
    {
        version = version.TrimStart('v', 'V');
        if (!Version.TryParse(version, out _) || version.Split('.').Length < 3)
            throw new ArgumentException("A release version such as 1.2.3 is required.", nameof(version));
        var prefix = runtime.Split('-')[0];
        if ((prefix == "win" && !OperatingSystem.IsWindows())
            || (prefix == "osx" && !OperatingSystem.IsMacOS())
            || (prefix == "linux" && !OperatingSystem.IsLinux()))
            throw new PlatformNotSupportedException($"Package {runtime} on a runner with the matching operating system.");

        var output = Path.Combine(paths.SolutionDirectory, "artifacts", "release");
        var host = Path.Combine(output, runtime, "host");
        var desktop = Path.Combine(output, runtime, "desktop");
        var packages = Path.Combine(output, "packages");
        Directory.CreateDirectory(packages);

        var hostArguments = new List<string>
        {
            "publish", "src/AI.Host/AI.Host.csproj", "--nologo", "-c", "Release",
            "-r", runtime, "--self-contained", "--output", host, $"-p:Version={version}"
        };
        if (prefix == "win") hostArguments.Add("-p:HostWindowsService=true");
        var result = await processes.RunAsync($"Publish Host {runtime}", "dotnet", hostArguments, cancellationToken);
        if (result != 0) return result;
        result = await processes.RunAsync($"Publish Desktop {runtime}", "dotnet",
            ["publish", "src/AI.Desktop/AI.Desktop.csproj", "--nologo", "-c", "Release",
                "-r", runtime, "--self-contained", "--output", desktop, $"-p:Version={version}"], cancellationToken);
        if (result != 0) return result;

        return prefix switch
        {
            "win" => await PackageWindowsAsync(runtime, version, host, desktop, packages, cancellationToken),
            "osx" => await PackageMacAsync(runtime, version, host, desktop, packages, cancellationToken),
            "linux" => await PackageLinuxAsync(runtime, version, host, desktop, packages, cancellationToken),
            _ => throw new ArgumentException($"Unsupported runtime: {runtime}", nameof(runtime))
        };
    }

    private async Task<int> PackageWindowsAsync(string runtime, string version, string host, string desktop,
        string packages, CancellationToken token)
    {
        var compiler = Environment.GetEnvironmentVariable("INNO_SETUP_COMPILER")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe");
        if (!File.Exists(compiler)) throw new FileNotFoundException("Inno Setup 6 is required.", compiler);
        foreach (var script in new[] { "install-host-task.ps1", "uninstall-host-task.ps1" })
            File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "windows", script),
                Path.Combine(host, script), true);
        var architecture = runtime.EndsWith("arm64", StringComparison.Ordinal) ? "arm64" : "x64compatible";
        foreach (var (product, source) in new[] { ("Host", host), ("Desktop", desktop) })
        {
            var script = Path.Combine(paths.SolutionDirectory, "build", "Packaging", "windows", $"{product}.iss");
            var result = await processes.RunAsync($"Package {product} {runtime}", compiler,
                [$"/DSourceDir={source}", $"/DOutputDir={packages}", $"/DBaseName=AI.{product}-{runtime}",
                    $"/DVersion={version}", $"/DArchitecture={architecture}", script], token);
            if (result != 0) return result;
        }
        return 0;
    }

    private async Task<int> PackageMacAsync(string runtime, string version, string host, string desktop,
        string packages, CancellationToken token)
    {
        var stage = Path.Combine(paths.SolutionDirectory, "artifacts", "release", runtime, "stage");
        var hostRoot = Path.Combine(stage, "host");
        var hostApp = Path.Combine(hostRoot, "Applications", "AI Client Host");
        CopyTree(host, hostApp);
        MakeExecutable(Path.Combine(hostApp, "AI.Host"));
        var agents = Path.Combine(hostRoot, "Library", "LaunchAgents");
        Directory.CreateDirectory(agents);
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "org.devteam.ai-client-host.plist"),
            Path.Combine(agents, "org.devteam.ai-client-host.plist"), true);
        var hostScripts = Path.Combine(stage, "host-scripts");
        Directory.CreateDirectory(hostScripts);
        var postinstall = Path.Combine(hostScripts, "postinstall");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "host-postinstall"),
            postinstall, true);
        MakeExecutable(postinstall);
        var result = await processes.RunAsync($"Package Host {runtime}", "pkgbuild",
            ["--root", hostRoot, "--scripts", hostScripts,
                "--identifier", "org.devteam.aiclient.host", "--version", version,
                Path.Combine(packages, $"AI.Host-{runtime}.pkg")], token);
        if (result != 0) return result;

        var desktopRoot = Path.Combine(stage, "desktop");
        var app = Path.Combine(desktopRoot, "Applications", "AI Client.app", "Contents");
        CopyTree(desktop, Path.Combine(app, "MacOS"));
        MakeExecutable(Path.Combine(app, "MacOS", "AI.Desktop"));
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "Desktop.Info.plist"),
            Path.Combine(app, "Info.plist"), true);
        return await processes.RunAsync($"Package Desktop {runtime}", "pkgbuild",
            ["--root", desktopRoot, "--identifier", "org.devteam.aiclient.desktop", "--version", version,
                Path.Combine(packages, $"AI.Desktop-{runtime}.pkg")], token);
    }

    private async Task<int> PackageLinuxAsync(string runtime, string version, string host, string desktop,
        string packages, CancellationToken token)
    {
        var stage = Path.Combine(paths.SolutionDirectory, "artifacts", "release", runtime, "stage");
        var architecture = runtime.EndsWith("arm64", StringComparison.Ordinal) ? "arm64" : "amd64";
        var hostRoot = Path.Combine(stage, "host");
        CopyTree(host, Path.Combine(hostRoot, "opt", "ai-client-host"));
        MakeExecutable(Path.Combine(hostRoot, "opt", "ai-client-host", "AI.Host"));
        var units = Path.Combine(hostRoot, "usr", "lib", "systemd", "user");
        Directory.CreateDirectory(units);
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "ai-client-host.service"),
            Path.Combine(units, "ai-client-host.service"), true);
        var enabled = Path.Combine(hostRoot, "etc", "systemd", "user", "default.target.wants");
        Directory.CreateDirectory(enabled);
        var link = Path.Combine(enabled, "ai-client-host.service");
        if (File.Exists(link)) File.Delete(link);
        File.CreateSymbolicLink(link, "/usr/lib/systemd/user/ai-client-host.service");
        // The launcher runs `AI.Host open`: the browser opens already connected to this Host.
        var hostApplications = Path.Combine(hostRoot, "usr", "share", "applications");
        Directory.CreateDirectory(hostApplications);
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "ai-client-host.desktop"),
            Path.Combine(hostApplications, "ai-client-host.desktop"), true);
        var hostPixmaps = Path.Combine(hostRoot, "usr", "share", "pixmaps");
        Directory.CreateDirectory(hostPixmaps);
        File.Copy(Path.Combine(paths.SolutionDirectory, "src", "AI.Desktop", "Assets", "app-icon.png"),
            Path.Combine(hostPixmaps, "ai-client-host.png"), true);
        WriteDebControl(hostRoot, "ai-client-host", version, architecture,
            "Local AI Client Host for the browser application");
        var postinst = Path.Combine(hostRoot, "DEBIAN", "postinst");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "host-postinst"),
            postinst, true);
        MakeExecutable(postinst);
        var result = await processes.RunAsync($"Package Host {runtime}", "dpkg-deb",
            ["--build", "--root-owner-group", hostRoot, Path.Combine(packages, $"AI.Host-{runtime}.deb")], token);
        if (result != 0) return result;

        var desktopRoot = Path.Combine(stage, "desktop");
        CopyTree(desktop, Path.Combine(desktopRoot, "opt", "ai-client-desktop"));
        MakeExecutable(Path.Combine(desktopRoot, "opt", "ai-client-desktop", "AI.Desktop"));
        var applications = Path.Combine(desktopRoot, "usr", "share", "applications");
        Directory.CreateDirectory(applications);
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "ai-client.desktop"),
            Path.Combine(applications, "ai-client.desktop"), true);
        var pixmaps = Path.Combine(desktopRoot, "usr", "share", "pixmaps");
        Directory.CreateDirectory(pixmaps);
        File.Copy(Path.Combine(paths.SolutionDirectory, "src", "AI.Desktop", "Assets", "app-icon.png"),
            Path.Combine(pixmaps, "ai-client.png"), true);
        WriteDebControl(desktopRoot, "ai-client-desktop", version, architecture,
            "AI Client desktop application");
        return await processes.RunAsync($"Package Desktop {runtime}", "dpkg-deb",
            ["--build", "--root-owner-group", desktopRoot, Path.Combine(packages, $"AI.Desktop-{runtime}.deb")], token);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void WriteDebControl(string root, string name, string version, string architecture,
        string description)
    {
        var directory = Path.Combine(root, "DEBIAN");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "control"),
            $"Package: {name}\nVersion: {version}\nArchitecture: {architecture}\nMaintainer: DevTeam <devteam@dev-team.org>\nDescription: {description}\n");
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
