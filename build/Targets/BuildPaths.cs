namespace Build.Targets;

internal sealed class BuildPaths : IBuildPaths
{
    public BuildPaths()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AI.slnx")))
                {
                    SolutionDirectory = directory.FullName;
                    HostOutputPath = Path.Combine(SolutionDirectory, "artifacts", "host");
                    return;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not find AI.slnx.");
    }

    public string SolutionDirectory { get; }

    public string HostOutputPath { get; }
}
