namespace AI.Search.Benchmarks;

using System.Globalization;
using System.Text;

/// <summary>
/// The corpus both tools are measured on: generated once, kept on disk, and identical for every
/// run, so a comparison is about the tools rather than about a directory somebody happened to have.
/// </summary>
/// <remarks>
/// The shape is chosen to be representative rather than large: source-like text of a few kilobytes
/// per file, a tree deep enough for the walk to matter, a repository marker with a
/// <c>.gitignore</c>, a folder the ignore file excludes, and two keywords — one that appears in
/// every file and one that appears in a handful — so a search that finds everything and a search
/// that finds almost nothing are both measured.
/// </remarks>
internal sealed record Corpus(string Root, int Files, long Bytes)
{
    /// <summary>A keyword that occurs in every generated file.</summary>
    public const string CommonKeyword = "SharedKeyword";

    /// <summary>A keyword that occurs in about one file in a hundred.</summary>
    public const string RareKeyword = "RareNeedle";

    /// <summary>The name of a directory the corpus ignore file excludes.</summary>
    public const string IgnoredDirectory = "generated";

    /// <summary>
    /// The corpus in <paramref name="root"/>, generated when it is not there yet or when
    /// <paramref name="regenerate"/> is set. The content is derived from the file's index, so two
    /// generated copies of one size hold the same text.
    /// </summary>
    public static Corpus Prepare(string root, int files, bool regenerate)
    {
        var marker = Path.Combine(root, ".corpus-complete");
        if (!regenerate && File.Exists(marker) && CountGenerated(root) == files)
        {
            return new Corpus(root, files, Measure(root));
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        // A repository marker, so an ignore-aware walk applies the corpus rules from the root.
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, ".gitignore"), IgnoredDirectory + "/\n*.log\n");

        for (var index = 0; index < files; index++)
        {
            // Ten files per directory and ten directories per level: a tree deep enough that a walk
            // has to descend rather than list one folder.
            var directory = Path.Combine(root, "d" + (index % 10).ToString("00", CultureInfo.InvariantCulture),
                "s" + (index / 10 % 10).ToString("00", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "file" + index.ToString("00000", CultureInfo.InvariantCulture) + ".cs"),
                Generate(index));
        }

        // A folder the ignore file excludes, holding a tenth of the content again: an ignore-aware
        // search skips it, and skipping it is part of what makes the two tools comparable.
        var ignored = Path.Combine(root, IgnoredDirectory);
        Directory.CreateDirectory(ignored);
        for (var index = 0; index < Math.Max(files / 10, 1); index++)
        {
            File.WriteAllText(Path.Combine(ignored, "generated" + index.ToString("0000", CultureInfo.InvariantCulture) + ".cs"),
                Generate(files + index));
        }

        // A log file, excluded by the same rules, and a hidden folder, skipped by both tools' defaults.
        File.WriteAllText(Path.Combine(root, "build.log"), Generate(files * 2));
        var hidden = Path.Combine(root, ".hidden");
        Directory.CreateDirectory(hidden);
        File.WriteAllText(Path.Combine(hidden, "notes.cs"), Generate(files * 3));

        File.WriteAllText(marker, files.ToString(CultureInfo.InvariantCulture));
        return new Corpus(root, files, Measure(root));
    }

    /// <summary>The corpus size in bytes, measured on disk, with the repository metadata left out.</summary>
    public static long Measure(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Sum(path => new FileInfo(path).Length);

    private static int CountGenerated(string root) =>
        Directory.Exists(root) ? Directory.EnumerateFiles(root, "file*.cs", SearchOption.AllDirectories).Count() : 0;

    private static string Generate(int index)
    {
        var builder = new StringBuilder(4096);
        builder.Append("namespace Corpus.Generated;\n\n");
        for (var type = 0; type < 6; type++)
        {
            builder.Append("/// <summary>Generated type ").Append(index).Append('.').Append(type)
                .Append(" of the benchmark corpus.</summary>\n");
            builder.Append("public sealed class Generated").Append(index).Append('_').Append(type).Append("\n{\n");
            builder.Append("    public const string Keyword = \"").Append(CommonKeyword).Append("\";\n");
            if (index % 100 == 0)
            {
                builder.Append("    public const string Rare = \"").Append(RareKeyword).Append("\";\n");
            }

            for (var member = 0; member < 8; member++)
            {
                builder.Append("    public int Value").Append(member).Append(" { get; set; } = ")
                    .Append((index * 31) + member).Append(";\n");
            }

            builder.Append("}\n\n");
        }

        return builder.ToString();
    }
}