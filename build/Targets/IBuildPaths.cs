namespace Build.Targets;

internal interface IBuildPaths
{
    string SolutionDirectory { get; }

    // `AI.Client.Host` writes its compiled binaries into a directory outside `src/` so a running
    // host instance never holds handles to files that `dotnet build` or `dotnet test` need to
    // overwrite. The path follows the existing `artifacts/<target>/` convention.
    string HostOutputPath { get; }
}
