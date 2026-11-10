namespace AI.Server.Hosting;

using AI.Contracts.FileSystem;

public interface ILocalModelPackageLocator
{
    Task<string> FindAsync(string installationDirectory, string? developmentBaseDirectory,
        string runtime, CancellationToken cancellationToken);
}

/// <summary>Lets a source-tree Desktop launch reuse the package prepared by the build target.</summary>
public sealed class LocalModelPackageLocator(IFileSystem files, IPath paths) : ILocalModelPackageLocator
{
    public async Task<string> FindAsync(string installationDirectory, string? developmentBaseDirectory,
        string runtime, CancellationToken cancellationToken)
    {
        if (developmentBaseDirectory is null
            || await files.FileExistsAsync(paths.Combine(installationDirectory, "manifest.json"), cancellationToken))
            return installationDirectory;
        var output = paths.GetFullPath(developmentBaseDirectory).TrimEnd(paths.DirectorySeparator, paths.AltDirectorySeparator);
        for (var root = paths.GetDirectoryName(output); root is not null; root = paths.GetDirectoryName(root))
        {
            if (!await files.FileExistsAsync(paths.Combine(root, "AI.slnx"), cancellationToken)) continue;
            var desktopBin = paths.Combine(root, paths.Combine("src", paths.Combine("AI.Desktop", "bin")))
                + paths.DirectorySeparator;
            // Test hosts, installed products, and other executables must never opt into this cache.
            if (!output.StartsWith(desktopBin, paths.Comparison)) return installationDirectory;
            var package = paths.Combine(root, paths.Combine("artifacts", paths.Combine("local-ai",
                paths.Combine("qwen3-q4-k-m-b12244", runtime))));
            return await files.FileExistsAsync(paths.Combine(package, "manifest.json"), cancellationToken)
                ? package : installationDirectory;
        }
        return installationDirectory;
    }
}
