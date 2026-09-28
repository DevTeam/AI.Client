namespace Build.Targets;

using System.Text.Json;

internal sealed class PublishWebTarget(IProcessRunner processRunner, IBuildPaths paths) : IPublishWebTarget
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var output = Path.Combine(paths.SolutionDirectory, "artifacts", "web");
        var result = await processRunner.RunAsync("Publish public Web", "dotnet",
            ["publish", "src/AI.Web/AI.Web.csproj", "--nologo", "-c", "Release", "--output", output],
            cancellationToken);
        if (result != 0) return result;

        var root = Path.Combine(output, "wwwroot");
        File.Copy(Path.Combine(paths.SolutionDirectory, "CNAME"), Path.Combine(root, "CNAME"), true);
        await File.WriteAllTextAsync(Path.Combine(root, ".nojekyll"), string.Empty, cancellationToken);
        File.Delete(Path.Combine(root, "appsettings.Development.json"));
        foreach (var file in Directory.GetFiles(root))
        {
            if (Path.GetFileName(file).StartsWith("appsettings", StringComparison.Ordinal)
                && (file.EndsWith(".json.br", StringComparison.Ordinal)
                    || file.EndsWith(".json.gz", StringComparison.Ordinal)))
                File.Delete(file);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "appsettings.json"),
            JsonSerializer.Serialize(new
            {
                ApiBaseUrl = "http://127.0.0.1:52173/",
                ClientMode = "PublicWeb"
            }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "404.html"),
            "<!doctype html><html><head><meta charset=\"utf-8\"><script>location.replace('/');</script></head></html>",
            cancellationToken);
        Console.WriteLine($"GitHub Pages content: {root}");
        return 0;
    }
}
