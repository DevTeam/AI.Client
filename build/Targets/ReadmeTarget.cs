namespace Build.Targets;

using System.Text;

internal sealed class ReadmeTarget(IBuildPaths paths, ITemplateEngine templateEngine) : IReadmeTarget
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await templateEngine.RenderAsync("/Templates/Readme.cshtml", "AI", buffer, cancellationToken);
        var text = Encoding.UTF8.GetString(buffer.ToArray());
        var path = Path.Combine(paths.SolutionDirectory, "README.md");
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), cancellationToken);
        Console.WriteLine($"Generated: {path}");
        return 0;
    }
}
