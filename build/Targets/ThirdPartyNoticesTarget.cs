namespace Build.Targets;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

/// <summary>
/// Lists every third-party component the Host and Desktop ship, with its license and copyright, and
/// the license texts they require. Packages come from the restore of the shipped projects, so a
/// version update changes the file; what NuGet does not know about (scripts in wwwroot, the font
/// inside a package, dictionaries, the .NET runtime of a self-contained build) is listed here.
/// </summary>
internal sealed class ThirdPartyNoticesTarget(IBuildPaths paths, IProcessRunner processes) : IThirdPartyNoticesTarget
{
    /// <summary>The projects whose output is installed: the two applications and the built-in MCP server.</summary>
    private static readonly string[] ShippedProjects =
        ["src/AI.Host/AI.Host.csproj", "src/AI.Desktop/AI.Desktop.csproj", "src/AI.Mcp.BuiltIn/AI.Mcp.BuiltIn.csproj"];

    /// <param name="Text">The license text when it is the package's own rather than a standard one.</param>
    /// <param name="Notices">A package's own third-party notices, for what it bundles (native Skia, HarfBuzz…).</param>
    private sealed record Component(string Name, string Version, string License, string Copyright, string Url, string? Text,
        string? Notices = null);

    public async Task<int> RunAsync(bool check, CancellationToken cancellationToken)
    {
        var components = new List<Component>();
        foreach (var project in ShippedProjects)
        {
            var result = await processes.RunAsync($"Restore {Path.GetFileNameWithoutExtension(project)}", "dotnet",
                ["restore", project, "--nologo"], cancellationToken);
            if (result != 0) return result;
            components.AddRange(Packages(Path.Combine(paths.SolutionDirectory, Path.GetDirectoryName(project)!, "obj", "project.assets.json")));
        }
        components.AddRange(Bundled());
        var text = Render(components.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Version, StringComparer.Ordinal).First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        var path = Path.Combine(paths.SolutionDirectory, "THIRD-PARTY-NOTICES.md");
        if (check)
        {
            var current = File.Exists(path) ? (await File.ReadAllTextAsync(path, cancellationToken)).ReplaceLineEndings("\n") : "";
            if (current == text) return 0;
            await Console.Error.WriteLineAsync("THIRD-PARTY-NOTICES.md is out of date. Run: dotnet run --project build -- third-party-notices",
                cancellationToken);
            return 1;
        }
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), cancellationToken);
        Console.WriteLine($"Generated: {path}");
        return 0;
    }

    /// <summary>Packages with assemblies or native files in the output; analyzers and build-only packages ship nothing.</summary>
    private static IEnumerable<Component> Packages(string assetsPath)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = assets.RootElement.GetProperty("packageFolders").EnumerateObject().First().Name;
        foreach (var target in assets.RootElement.GetProperty("targets").EnumerateObject())
        foreach (var library in target.Value.EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package"
                || !(library.Value.TryGetProperty("runtime", out var runtime) && Ships(runtime)
                     || library.Value.TryGetProperty("native", out _) || library.Value.TryGetProperty("runtimeTargets", out _)))
                continue;
            var (id, version) = (library.Name.Split('/')[0], library.Name.Split('/')[1]);
            yield return Describe(Path.Combine(root, id.ToLowerInvariant(), version.ToLowerInvariant()), id, version);
        }
    }

    /// <summary>A runtime entry of only "_._" is a placeholder: the package adds no file at run time.</summary>
    private static bool Ships(JsonElement runtime) => runtime.EnumerateObject().Any(item => !item.Name.EndsWith("_._", StringComparison.Ordinal));

    private static Component Describe(string directory, string id, string version)
    {
        var nuspec = XDocument.Load(Directory.EnumerateFiles(directory, "*.nuspec").Single());
        var metadata = nuspec.Root!.Elements().Single(item => item.Name.LocalName == "metadata");
        string Value(string name) => metadata.Elements().FirstOrDefault(item => item.Name.LocalName == name)?.Value.Trim() ?? "";
        var license = metadata.Elements().FirstOrDefault(item => item.Name.LocalName == "license");
        var copyright = Value("copyright") is { Length: > 0 } stated ? stated : Value("authors");
        // A redirect link loses its target without its query, so such a package links to its NuGet page.
        var url = Value("projectUrl") is { Length: > 0 } project && !project.Contains("fwlink", StringComparison.OrdinalIgnoreCase)
            ? project.Split('?')[0] : $"https://www.nuget.org/packages/{id}";
        var notices = Directory.EnumerateFiles(directory)
            .FirstOrDefault(path => Path.GetFileName(path).StartsWith("THIRD-PARTY-NOTICES", StringComparison.OrdinalIgnoreCase)) is { } file
            ? File.ReadAllText(file).ReplaceLineEndings("\n").Trim() : null;
        return license?.Attribute("type")?.Value == "file"
            ? new(id, version, "See license text", copyright, url,
                File.ReadAllText(Path.Combine(directory, license.Value.Trim())).ReplaceLineEndings("\n").Trim(), notices)
            : new(id, version, license?.Value.Trim() is { Length: > 0 } expression ? expression : Value("licenseUrl"), copyright, url,
                null, notices);
    }

    private IEnumerable<Component> Bundled()
    {
        string Notice(string name) => File.ReadAllText(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "notices", name))
            .ReplaceLineEndings("\n").Trim();
        string Source(string path) => File.ReadAllText(Path.Combine(paths.SolutionDirectory, path)).ReplaceLineEndings("\n").Trim();
        yield return new(".NET runtime and ASP.NET Core (self-contained builds)", "11.0", "MIT",
            "Copyright (c) .NET Foundation and Contributors", "https://github.com/dotnet/runtime", null);
        yield return new("mermaid (wwwroot/js/vendor/mermaid.min.js)", "bundled", "MIT",
            "Copyright (c) 2014 - 2022 Knut Sveidqvist", "https://github.com/mermaid-js/mermaid", Notice("mermaid-MIT.txt"));
        yield return new("highlight.js (wwwroot/js/vendor)", "bundled", "BSD-3-Clause",
            "Copyright (c) 2006, Ivan Sagalaev", "https://highlightjs.org/", Source("src/AI.Web/wwwroot/js/vendor/highlight.LICENSE"));
        yield return new("Inter font (inside Avalonia.Fonts.Inter)", "bundled", "OFL-1.1",
            "Copyright (c) 2016 The Inter Project Authors", "https://github.com/rsms/inter", Notice("Inter-OFL.txt"));
        yield return new("json-everything source (JsonSchema.Net, JsonPointer.Net, Json.More.Net)", "bundled", "MIT",
            "Copyright (c) .NET Foundation and Contributors", "https://github.com/json-everything/json-everything",
            Notice("json-everything-MIT.txt"));
        foreach (var language in new[] { "en", "es", "fr", "ru" })
            yield return new($"Hunspell dictionary ({language}, Dictionaries/{language})", "bundled", "See license text", "", "",
                Source($"src/AI.TextCorrection/Dictionaries/{language}/license"));
    }

    private string Render(IReadOnlyList<Component> components)
    {
        var text = new StringBuilder();
        text.Append("# Third-party notices\n\n")
            .Append("AI Client is licensed under the Apache License 2.0 (see LICENSE and NOTICE). It includes the third-party\n")
            .Append("components below, each under its own license. The optional Local AI component lists its model and\n")
            .Append("runtime in local-ai/THIRD-PARTY-NOTICES.txt.\n\n")
            .Append("This file is generated by `dotnet run --project build -- third-party-notices`; do not edit it by hand.\n\n")
            .Append("## Components\n\n| Component | Version | License | Copyright |\n| --- | --- | --- | --- |\n");
        foreach (var item in components)
        {
            var name = item.Url.Length > 0 ? $"[{item.Name}]({item.Url})" : item.Name;
            text.Append(CultureInfo.InvariantCulture, $"| {name} | {item.Version} | {item.License} | {item.Copyright.Replace("|", "\\|", StringComparison.Ordinal)} |\n");
        }
        text.Append("\n## License texts\n");
        var templates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MIT"] = "MIT.txt", ["BSD-2-Clause"] = "BSD-2-Clause.txt"
        };
        foreach (var group in components.Where(item => item.Text is null && templates.ContainsKey(item.License))
                     .GroupBy(item => item.License).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n### {group.Key}\n\nApplies to the components above licensed under {group.Key}, each with its copyright notice:\n\n");
            foreach (var item in group) text.Append(CultureInfo.InvariantCulture, $"- {item.Name}: {item.Copyright}\n");
            text.Append("\n```text\n")
                .Append(File.ReadAllText(Path.Combine(paths.SolutionDirectory, "build", "Packaging", "notices", templates[group.Key]))
                    .ReplaceLineEndings("\n").Trim())
                .Append("\n```\n");
        }
        if (components.Any(item => item.Text is null && item.License == "Apache-2.0"))
        {
            text.Append("\n### Apache-2.0\n\nApplies to: ")
                .Append(string.Join(", ", components.Where(item => item.Text is null && item.License == "Apache-2.0").Select(item => item.Name)))
                .Append(". The license text is the same as AI Client's own, in LICENSE.\n");
        }
        foreach (var group in components.Where(item => item.Text is not null).GroupBy(item => item.Text!))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n### {string.Join(", ", group.Select(item => item.Name))}\n\n```text\n").Append(group.Key).Append("\n```\n");
        }
        foreach (var group in components.Where(item => item.Notices is not null).GroupBy(item => item.Notices!))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n### Notices shipped with {string.Join(", ", group.Select(item => item.Name))}\n\n```text\n")
                .Append(group.Key).Append("\n```\n");
        }
        var unknown = components.Where(item => item.Text is null && !templates.ContainsKey(item.License) && item.License != "Apache-2.0").ToArray();
        if (unknown.Length > 0)
            throw new InvalidDataException("No license text for: " + string.Join(", ", unknown.Select(item => $"{item.Name} ({item.License})")));
        return text.ToString();
    }
}
