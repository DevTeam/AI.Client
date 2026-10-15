namespace AI.Application.Tests.Tools.Commands;

using AI.Application.Tools.Commands;
using AI.Contracts.FileSystem;

/// <summary>
/// A PATH holding git, dotnet and pwsh in one tools directory, a project beside it, and a stray
/// git inside the project.
/// </summary>
internal static class TestPrograms
{
    public static readonly IPath Paths = new SystemPath();
    public static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "command-rules"));
    public static readonly string Project = Path.Combine(Root, "project");
    public static readonly string Tools = Path.Combine(Root, "tools");
    public static readonly string Git = Path.Combine(Tools, Program("git"));
    public static readonly string Dotnet = Path.Combine(Tools, Program("dotnet"));
    public static readonly string Pwsh = Path.Combine(Tools, Program("pwsh"));
    public static readonly string LocalGit = Path.Combine(Project, Program("git"));
    public static readonly IReadOnlyList<CommandGrant> Grants = [new(Project, true)];

    public static ICommandParser Parser() => new CommandParser(new Locator(), Paths);

    private static string Program(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private sealed class Locator : IExecutableLocator
    {
        private static readonly string[] Installed = [Git, Dotnet, Pwsh];

        public Task<string?> LocateAsync(string executable, string? workingDirectory, CancellationToken cancellationToken)
        {
            if (Paths.IsFullyQualified(executable))
                return Task.FromResult(Installed.Append(LocalGit)
                    .FirstOrDefault(file => string.Equals(file, executable, Paths.Comparison)));
            var name = CommandNames.Normalize(executable, Paths.IsCaseSensitive);
            return Task.FromResult(Installed.FirstOrDefault(file => CommandNames.Normalize(file, Paths.IsCaseSensitive) == name));
        }
    }
}
