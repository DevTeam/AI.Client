using System.Text.Json;
using AI.Mcp.BuiltIn.Process;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;

// Deterministic CLI diagnostics through the actual process_run implementation, without a model.
// Configurations and caches are synthetic: never grant the user's profile or copy credentials.
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This probe currently exercises Windows cmd and AppContainer only.");
    return 2;
}

var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("evals", "results", $"cli-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));
var workspace = Directory.CreateDirectory(Path.Combine(output, "workspace")).FullName;
var temporary = Directory.CreateDirectory(Path.Combine(output, "temporary")).FullName;
var closed = Directory.CreateDirectory(Path.Combine(output, "closed")).FullName;
var ghConfig = Directory.CreateDirectory(Path.Combine(closed, "gh")).FullName;
var npmCache = Directory.CreateDirectory(Path.Combine(closed, "npm")).FullName;
File.WriteAllText(Path.Combine(ghConfig, "config.yml"), "git_protocol: ssh\n");
var gitConfig = Path.Combine(closed, "gitconfig");
File.WriteAllText(gitConfig, "[cliEval]\n    marker = synthetic-config\n");
var sentinel = Path.Combine(closed, "sentinel.txt");
File.WriteAllText(sentinel, "closed-sentinel");
File.WriteAllText(Path.Combine(workspace, "read.cjs"), "console.log(require('node:fs').readFileSync(process.argv[2], 'utf8'));\n");
File.WriteAllText(Path.Combine(workspace, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
File.WriteAllText(Path.Combine(workspace, "Program.cs"), "System.Console.WriteLine(\"cli-eval-ok\");");
File.WriteAllText(Path.Combine(workspace, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");

var launcher = new ProcessLauncher();
if (!launcher.Support.Available)
{
    Console.Error.WriteLine(launcher.Support.Reason);
    return 2;
}

var paths = new SandboxPath[]
{
    new(workspace, SandboxAccess.ReadWrite),
    new(temporary, SandboxAccess.ReadWrite)
};
var results = new List<ProbeResult>();
var versions = new Dictionary<string, ProcessResult>(StringComparer.Ordinal);
foreach (var (name, command) in new[]
{
    ("dotnet", "dotnet --version"), ("git", "git --version"), ("gh", "gh --version"),
    ("node", "node --version"), ("npm", "npm.cmd --version"), ("pwsh", "pwsh -NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"")
})
{
    versions[name] = await Run(command, paths);
    Console.WriteLine($"{name}: exit {versions[name].ExitCode}; {versions[name].Error}");
}

if (Available("dotnet"))
{
    // Reproduce the reported fresh-home first-run behavior, then disable persistent PATH edits.
    var home = Directory.CreateDirectory(Path.Combine(temporary, "dotnet-home")).FullName;
    var dotnet = $"set \"DOTNET_CLI_HOME={home}\"&& set \"DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1\"&& set \"DOTNET_NOLOGO=1\"&& ";
    await Probe("dotnet", "fresh-home", dotnet + "dotnet new console --output first-run --no-restore");
    var prepared = dotnet + $"set \"DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0\"&& set \"DOTNET_GENERATE_ASPNET_CERTIFICATE=0\"&& set \"NUGET_PACKAGES={Path.Combine(temporary, "nuget")}\"&& ";
    await Probe("dotnet", "prepared-build", prepared + "dotnet build Probe.csproj --disable-build-servers -m:1 --nologo -v q -p:DefaultItemExcludes=first-run/**");
    await Probe("dotnet", "explicit-restore-config", prepared + "dotnet restore Probe.csproj --configfile NuGet.Config --disable-build-servers --nologo -v q && dotnet build Probe.csproj --no-restore --disable-build-servers -m:1 --nologo -v q -p:DefaultItemExcludes=first-run/**", required: true);
    var appdata = Directory.CreateDirectory(Path.Combine(temporary, "appdata")).FullName;
    await Probe("dotnet", "redirected-appdata", prepared + $"set \"APPDATA={appdata}\"&& set \"LOCALAPPDATA={appdata}\"&& dotnet build Probe.csproj --disable-build-servers -m:1 --nologo -v q -p:DefaultItemExcludes=first-run/**", required: true);
}

if (Available("git"))
{
    var command = $"set \"GIT_CONFIG_GLOBAL={gitConfig}\"&& git config --global --get cliEval.marker";
    await Probe("git", "closed-config", command);
    await Probe("git", "granted-config", command, [new(gitConfig, SandboxAccess.Read)], required: true, expectedText: "synthetic-config");
    await Probe("git", "explicit-directory", $"set \"GIT_CONFIG_GLOBAL={gitConfig}\"&& git -C \"{workspace}\" config --global --get cliEval.marker", [new(gitConfig, SandboxAccess.Read)]);
}

if (Available("gh"))
{
    var command = $"set \"GH_CONFIG_DIR={ghConfig}\"&& gh config get git_protocol";
    await Probe("gh", "closed-config", command);
    await Probe("gh", "granted-config", command, [new(ghConfig, SandboxAccess.Read)], required: true, expectedText: "ssh");
}

if (Available("npm"))
{
    await Probe("npm", "closed-cache", $"npm.cmd cache verify --cache \"{npmCache}\"");
    await Probe("npm", "redirected-cache", $"set \"npm_config_cache={Path.Combine(temporary, "npm")}\"&& npm.cmd cache verify", required: true);
}

if (Available("node"))
{
    var command = $"node read.cjs \"{sentinel}\"";
    await Probe("node", "closed-file", command);
    await Probe("node", "granted-file", command, [new(sentinel, SandboxAccess.Read)], required: true);
    await DirectProbe("node", "direct-version", ["--version"]);
    await DirectProbe("node", "direct-granted-file", ["read.cjs", sentinel], [new(sentinel, SandboxAccess.Read)], expectedText: "closed-sentinel");
    if (Available("npm"))
    {
        var node = Find("node");
        var npm = Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js");
        if (File.Exists(npm))
        {
            await DirectProbe("node", "npm-closed-cache", [npm, "cache", "verify", "--cache", npmCache], required: false);
            await DirectProbe("node", "npm-redirected-cache", [npm, "cache", "verify", "--cache", Path.Combine(temporary, "npm"), "--userconfig", Path.Combine(workspace, "empty.npmrc")]);
        }
    }
}

if (Available("pwsh"))
{
    var command = $"pwsh -NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; Get-Content -LiteralPath '{sentinel}'\"";
    await Probe("pwsh", "closed-file", command);
    await Probe("pwsh", "granted-file", command, [new(sentinel, SandboxAccess.Read)], required: true);
    await DirectProbe("pwsh", "direct-version", ["-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.ToString()"]);
    await DirectProbe("pwsh", "direct-granted-file", ["-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference='Stop'; Get-Content -LiteralPath '{sentinel}'"], [new(sentinel, SandboxAccess.Read)], expectedText: "closed-sentinel");
}

// Recheck after all targeted grants: capabilities of earlier launches must not leak into this one.
await Probe("cmd", "closed-after-grants", $"type \"{sentinel}\"", mustFail: true);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { launcher.Support, versions, results }, json));
var lines = new List<string>
{
    "# Windows CLI sandbox probes", "", $"Mechanism: {launcher.Support.Mechanism}. Null device available: {launcher.Support.OpensNullDevice}.", "",
    "All commands ran through ProcessRunner with a sandbox. Configuration is synthetic; authentication is not tested.", "",
    "| CLI | Case | Exit | Timeout | Limit | Required outcome met |", "| --- | --- | --- | --- | --- | --- |"
};
lines.AddRange(results.Select(row => $"| {row.Tool} | {row.Case} | {row.Result.ExitCode} | {row.Result.TimedOut} | {row.Result.LimitExceeded} | {row.OutcomeMet?.ToString() ?? "observation"} |"));
File.WriteAllLines(Path.Combine(output, "summary.md"), lines);
Console.WriteLine($"Results: {output}");
return results.All(row => row.OutcomeMet != false) ? 0 : 1;

bool Available(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "")
    .Split(Path.PathSeparator)
    .Any(directory => File.Exists(Path.Combine(directory.Trim('"'), tool + (tool == "npm" ? ".cmd" : ".exe"))));

Task<ProcessResult> Run(string command, IReadOnlyList<SandboxPath> grants)
{
    // A batch file keeps cmd syntax out of the C-runtime quoting applied to ArgumentList.
    var script = $"probe-{Guid.NewGuid():N}.cmd";
    File.WriteAllText(Path.Combine(workspace, script), "@echo off\r\n" + command + "\r\n");
    return new ProcessRunner(launcher: launcher, sandbox: new ProbeSandbox(new SandboxPolicy(grants), temporary))
        .RunAsync(new ProcessRequest("cmd.exe", ["/d", "/c", script], workspace, TimeoutMs: 90000), CancellationToken.None);
}

async Task Probe(string tool, string name, string command, SandboxPath[]? extra = null, bool required = false, bool mustFail = false, string? expectedText = null)
{
    var grants = paths.Concat(extra ?? []).ToArray();
    var result = await Run(command, grants);
    var completed = result is { ExitCode: not null, TimedOut: false, Error: null, LimitExceeded: null };
    bool? met = !required && !mustFail ? null
        : completed && (mustFail ? result.ExitCode != 0 : result.ExitCode == 0)
          && (expectedText is null || result.Stdout.Contains(expectedText, StringComparison.Ordinal));
    results.Add(new ProbeResult(tool, name, command, grants, result, met));
    Console.WriteLine($"{tool}/{name}: exit {result.ExitCode}, {result.DurationMs} ms, outcome {met}");
}

string Find(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
    .Select(directory => Path.Combine(directory.Trim('"'), tool + ".exe"))
    .First(File.Exists);

async Task DirectProbe(string tool, string name, string[] arguments, SandboxPath[]? extra = null, bool required = true, string? expectedText = null)
{
    var executable = Find(tool);
    var grants = paths.Concat(extra ?? []).ToArray();
    try
    {
        var result = await new ProcessRunner(launcher: launcher, sandbox: new ProbeSandbox(new SandboxPolicy(grants), temporary))
            .RunAsync(new ProcessRequest(executable, arguments, workspace, TimeoutMs: 90000), CancellationToken.None);
        bool? met = !required ? null : result is { ExitCode: 0, TimedOut: false, Error: null, LimitExceeded: null }
            && (expectedText is null || result.Stdout.Contains(expectedText, StringComparison.Ordinal));
        results.Add(new ProbeResult(tool, name, JsonSerializer.Serialize(new { executable, arguments }), grants, result, met));
        Console.WriteLine($"{tool}/{name}: exit {result.ExitCode}; {result.Error}");
    }
    catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.WriteLine($"{tool}/{name}: {error.Message}");
        results.Add(new ProbeResult(tool, name, executable, paths, new ProcessResult(null, "", "", 0, false, false, error.Message), false));
    }
}

internal sealed record ProbeResult(string Tool, string Case, string Command, SandboxPath[] Grants, ProcessResult Result, bool? OutcomeMet);

internal sealed class ProbeSandbox(SandboxPolicy policy, string temporary) : IToolSandbox
{
    public SandboxPolicy Policy(SandboxedTool tool) => policy;
    public string TemporaryDirectory => temporary;
    public string Describe(SandboxedTool tool) => "Synthetic CLI evaluation sandbox.";
}
