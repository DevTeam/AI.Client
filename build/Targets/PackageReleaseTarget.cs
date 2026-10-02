namespace Build.Targets;

using System.Xml.Linq;

/// <summary>Builds both self-contained products and their native packages on the target OS runner.</summary>
internal sealed class PackageReleaseTarget(IProcessRunner processes, IBuildPaths paths) : IPackageReleaseTarget
{
    public async Task<int> RunAsync(string runtime, string version, CancellationToken cancellationToken)
    {
        if (runtime is not ("win-x64" or "win-arm64" or "osx-x64" or "osx-arm64" or "linux-x64" or "linux-arm64"))
            throw new ArgumentException($"Unsupported runtime: {runtime}", nameof(runtime));
        version = version.TrimStart('v', 'V');
        // `Version.TryParse` rejects SemVer pre-release and build metadata suffixes such as
        // `-dev` or `+build.5`. The release workflow and the local installers both rely on those
        // suffixes for non-final builds, so accept `<major>.<minor>.<patch>[-+<suffix>]` explicitly.
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:[-+].*)?$"))
            throw new ArgumentException("A release version such as 1.2.3 or 1.2.3-rc.1 is required.", nameof(version));
        var prefix = runtime.Split('-')[0];
        if ((prefix == "win" && !OperatingSystem.IsWindows())
            || (prefix == "osx" && !OperatingSystem.IsMacOS())
            || (prefix == "linux" && !OperatingSystem.IsLinux()))
            throw new PlatformNotSupportedException($"Package {runtime} on a runner with the matching operating system.");

        var output = Path.Combine(paths.SolutionDirectory, "artifacts", "release");
        var host = Path.Combine(output, runtime, "host");
        var desktop = Path.Combine(output, runtime, "desktop");
        var csharp = Path.Combine(output, runtime, "csharp-mcp");
        var packages = Path.Combine(output, "packages");
        Directory.CreateDirectory(packages);
        // Publish and staging directories contain only generated files. Start with an empty
        // payload so repeated releases cannot retain an optional component from an earlier run.
        foreach (var directory in new[] { host, desktop, csharp, Path.Combine(output, runtime, "stage") })
        {
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("The generated payload must be inside artifacts/release.");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        var hostArguments = new List<string>
        {
            "publish", "src/AI.Host/AI.Host.csproj", "--nologo", "-c", "Release",
            "-r", runtime, "--self-contained", "--output", host, $"-p:Version={version}", "-p:IncludeCSharpMcp=false"
        };
        if (prefix == "win") hostArguments.Add("-p:HostWindowsService=true");
        var result = await processes.RunAsync($"Publish Host {runtime}", "dotnet", hostArguments, cancellationToken);
        if (result != 0) return result;
        result = await processes.RunAsync($"Publish Desktop {runtime}", "dotnet",
            ["publish", "src/AI.Desktop/AI.Desktop.csproj", "--nologo", "-c", "Release",
                "-r", runtime, "--self-contained", "--output", desktop, $"-p:Version={version}", "-p:IncludeCSharpMcp=false"], cancellationToken);
        if (result != 0) return result;
        // The C# scripting server publishes independently; Inno Setup consumes it as a subcomponent
        // of Host and Desktop, macOS offers a package choice, and Linux ships a companion package.
        result = await processes.RunAsync($"Publish CSharp MCP {runtime}", "dotnet",
            ["publish", "src/AI.Mcp.CSharp/AI.Mcp.CSharp.csproj", "--nologo", "-c", "Release",
                "-r", runtime, "--self-contained", "--output", csharp, $"-p:Version={version}"], cancellationToken);
        if (result != 0) return result;

        foreach (var (product, directory) in new[] { ("Host", host), ("Desktop", desktop) })
        {
            if (!File.Exists(Path.Combine(directory, "wwwroot", "index.html"))
                || !File.Exists(Path.Combine(directory, "wwwroot", "_framework", "blazor.webassembly.js")))
                throw new InvalidDataException($"{product} {runtime} publish does not contain the Web interface.");
        }

        var csharpExecutable = "AI.Mcp.CSharp" + (OperatingSystem.IsWindows() ? ".exe" : "");
        if (!File.Exists(Path.Combine(csharp, csharpExecutable)))
            throw new InvalidDataException($"CSharp MCP {runtime} publish does not contain {csharpExecutable}.");

        return prefix switch
        {
            "win" => await PackageWindowsAsync(runtime, version, host, desktop, csharp, packages, cancellationToken),
            "osx" => await PackageMacAsync(runtime, version, host, desktop, csharp, packages, cancellationToken),
            "linux" => await PackageLinuxAsync(runtime, version, host, desktop, csharp, packages, cancellationToken),
            _ => throw new ArgumentException($"Unsupported runtime: {runtime}", nameof(runtime))
        };
    }

    private async Task<int> PackageWindowsAsync(string runtime, string version, string host, string desktop,
        string csharp, string packages, CancellationToken token)
    {
        var compiler = Environment.GetEnvironmentVariable("INNO_SETUP_COMPILER")
            ?? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe")
            }.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Inno Setup 6 is required. Install it or set INNO_SETUP_COMPILER to ISCC.exe.");
        if (!File.Exists(compiler)) throw new FileNotFoundException("Inno Setup 6 is required.", compiler);
        foreach (var script in new[] { "install-host-task.ps1", "stop-installed-app.ps1" })
            File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "windows", script),
                Path.Combine(host, script), true);
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "windows", "stop-installed-app.ps1"),
            Path.Combine(desktop, "stop-installed-app.ps1"), true);
        var architecture = runtime.EndsWith("arm64", StringComparison.Ordinal) ? "arm64" : "x64compatible";
        foreach (var (product, source) in new[] { ("Host", host), ("Desktop", desktop) })
        {
            var script = Path.Combine(paths.SolutionDirectory, "build", "Packaging", "windows", $"{product}.iss");
            var result = await processes.RunAsync($"Package {product} {runtime}", compiler,
                [$"/DSourceDir={source}", $"/DCSharpSourceDir={csharp}", $"/DOutputDir={packages}", $"/DBaseName=AI.{product}-{runtime}",
                    $"/DVersion={version}", $"/DArchitecture={architecture}", script], token);
            if (result != 0) return result;
        }
        return 0;
    }

    private async Task<int> PackageMacAsync(string runtime, string version, string host, string desktop,
        string csharp, string packages, CancellationToken token)
    {
        var stage = Path.Combine(paths.SolutionDirectory, "artifacts", "release", runtime, "stage");
        var hostRoot = Path.Combine(stage, "host");
        var hostApp = Path.Combine(hostRoot, "Applications", "AI Client Host");
        CopyTree(host, hostApp);
        MakeExecutable(Path.Combine(hostApp, "AI.Host"));
        var uninstallHost = Path.Combine(hostApp, "uninstall.sh");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "uninstall-host"), uninstallHost, true);
        MakeExecutable(uninstallHost);
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
        var preinstall = Path.Combine(hostScripts, "preinstall");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "host-preinstall"),
            preinstall, true);
        MakeExecutable(preinstall);
        var result = await processes.RunAsync($"Package Host {runtime}", "pkgbuild",
            ["--root", hostRoot, "--scripts", hostScripts,
                "--identifier", "org.devteam.aiclient.host", "--version", version,
                Path.Combine(stage, "AI.Host.pkg")], token);
        if (result != 0) return result;

        var desktopRoot = Path.Combine(stage, "desktop");
        var app = Path.Combine(desktopRoot, "Applications", "AI Client.app", "Contents");
        CopyTree(desktop, Path.Combine(app, "MacOS"));
        MakeExecutable(Path.Combine(app, "MacOS", "AI.Desktop"));
        var uninstallDesktop = Path.Combine(app, "MacOS", "uninstall.sh");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "uninstall-desktop"),
            uninstallDesktop, true);
        MakeExecutable(uninstallDesktop);
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "Desktop.Info.plist"),
            Path.Combine(app, "Info.plist"), true);
        var desktopScripts = Path.Combine(stage, "desktop-scripts");
        Directory.CreateDirectory(desktopScripts);
        preinstall = Path.Combine(desktopScripts, "preinstall");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "desktop-preinstall"),
            preinstall, true);
        MakeExecutable(preinstall);
        result = await processes.RunAsync($"Package Desktop {runtime}", "pkgbuild",
            ["--root", desktopRoot, "--scripts", desktopScripts,
                "--identifier", "org.devteam.aiclient.desktop", "--version", version,
                Path.Combine(stage, "AI.Desktop.pkg")], token);
        if (result != 0) return result;

        // Both installers share one optional package and receipt. Runtime discovery uses this
        // shared location, so installation order and updates to either application do not matter.
        var csharpRoot = Path.Combine(stage, "csharp-mcp");
        CopyTree(csharp, Path.Combine(csharpRoot, "Library", "Application Support", "AI Client", "McpCsharp"));
        MakeExecutable(Path.Combine(csharpRoot, "Library", "Application Support", "AI Client", "McpCsharp", "AI.Mcp.CSharp"));
        var csharpUninstall = Path.Combine(csharpRoot, "Library", "Application Support", "AI Client", "McpCsharp", "uninstall.sh");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "macos", "uninstall-csharp-mcp"),
            csharpUninstall, true);
        MakeExecutable(csharpUninstall);
        result = await processes.RunAsync($"Package CSharp MCP {runtime}", "pkgbuild",
            ["--root", csharpRoot,
                "--identifier", "org.devteam.aiclient.csharpmcp", "--version", version,
                Path.Combine(stage, "AI.Mcp.CSharp.pkg")], token);
        if (result != 0) return result;

        foreach (var product in new[] { "Host", "Desktop" })
        {
            var distribution = Path.Combine(stage, $"{product}.xml");
            WriteMacDistribution(distribution, product, version);
            result = await processes.RunAsync($"Build {product} installer {runtime}", "productbuild",
                ["--distribution", distribution, "--package-path", stage,
                    Path.Combine(packages, $"AI.{product}-{runtime}.pkg")], token);
            if (result != 0) return result;
        }
        return 0;
    }

    private async Task<int> PackageLinuxAsync(string runtime, string version, string host, string desktop,
        string csharp, string packages, CancellationToken token)
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
        foreach (var script in new[] { "preinst", "prerm" })
        {
            var target = Path.Combine(hostRoot, "DEBIAN", script);
            File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "host-stop"), target, true);
            MakeExecutable(target);
        }
        var postrm = Path.Combine(hostRoot, "DEBIAN", "postrm");
        File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "host-postrm"), postrm, true);
        MakeExecutable(postrm);
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
        foreach (var script in new[] { "preinst", "prerm" })
        {
            var target = Path.Combine(desktopRoot, "DEBIAN", script);
            File.Copy(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "linux", "desktop-stop"), target, true);
            MakeExecutable(target);
        }
        result = await processes.RunAsync($"Package Desktop {runtime}", "dpkg-deb",
            ["--build", "--root-owner-group", desktopRoot, Path.Combine(packages, $"AI.Desktop-{runtime}.deb")], token);
        if (result != 0) return result;

        // Linux package managers offer the scripting server as an optional companion. Both
        // applications discover it in this shared directory, including when it is installed first.
        var csharpRoot = Path.Combine(stage, "csharp-mcp");
        CopyTree(csharp, Path.Combine(csharpRoot, "opt", "ai-client-csharp-mcp"));
        MakeExecutable(Path.Combine(csharpRoot, "opt", "ai-client-csharp-mcp", "AI.Mcp.CSharp"));
        WriteDebControl(csharpRoot, "ai-client-csharp-mcp", version, architecture,
            "Optional C# scripting MCP server for the AI Client");
        return await processes.RunAsync($"Package CSharp MCP {runtime}", "dpkg-deb",
            ["--build", "--root-owner-group", csharpRoot, Path.Combine(packages, $"AI.Mcp.CSharp-{runtime}.deb")], token);
    }

    private static void WriteMacDistribution(string path, string product, string version)
    {
        var mainId = $"org.devteam.aiclient.{product.ToLowerInvariant()}";
        const string csharpId = "org.devteam.aiclient.csharpmcp";
        new XDocument(new XElement("installer-gui-script", new XAttribute("minSpecVersion", "1"),
            new XElement("title", $"AI Client {product}"),
            new XElement("options", new XAttribute("customize", "always"), new XAttribute("require-scripts", "false")),
            new XElement("choices-outline",
                new XElement("line", new XAttribute("choice", "main")),
                new XElement("line", new XAttribute("choice", "csharp"))),
            new XElement("choice", new XAttribute("id", "main"), new XAttribute("title", $"AI Client {product}"),
                new XAttribute("enabled", "false"), new XAttribute("selected", "true"),
                new XElement("pkg-ref", new XAttribute("id", mainId))),
            new XElement("choice", new XAttribute("id", "csharp"), new XAttribute("title", "C# scripting tools"),
                new XAttribute("description", "Optional MCP server that compiles and runs C# scripts with Roslyn."),
                new XAttribute("start_selected", "false"),
                new XElement("pkg-ref", new XAttribute("id", csharpId))),
            new XElement("pkg-ref", new XAttribute("id", mainId), new XAttribute("version", version), $"AI.{product}.pkg"),
            new XElement("pkg-ref", new XAttribute("id", csharpId), new XAttribute("version", version), "AI.Mcp.CSharp.pkg")))
            .Save(path);
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
            $"Package: {name}\nVersion: {version}\nArchitecture: {architecture}\nMaintainer: DevTeam <devteam@dev-team.org>\nDescription: {description}\n"
            + (name is "ai-client-host" or "ai-client-desktop" ? "Suggests: ai-client-csharp-mcp\n" : ""));
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
