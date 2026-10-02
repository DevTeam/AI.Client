namespace Build.Targets;

using System.Security.Cryptography;
using System.Text;
using AI.TextCorrection.Resources;

internal sealed class PrepareTextCorrectionTarget(IBuildPaths paths, ITrigramIndexFormat format) : IPrepareTextCorrectionTarget
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var sources = Path.Combine(paths.SolutionDirectory, "src", "AI.TextCorrection", "Dictionaries");
        foreach (var source in Directory.EnumerateFiles(sources, "index.dic", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var language = Path.GetFileName(Path.GetDirectoryName(source))!;
            var index = new HashSet<ulong>();
            using (var reader = new StreamReader(source, new UTF8Encoding(false, true)))
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                    format.AddWord(line.AsSpan(), index);
            await using var input = File.OpenRead(source);
            var fingerprint = await SHA256.HashDataAsync(input, cancellationToken);
            using var buffer = new MemoryStream();
            format.Write(buffer, index, fingerprint);
            var data = buffer.ToArray();
            var output = Path.ChangeExtension(source, ".trigrams");
            // A repeated manual run leaves unchanged resources intact.
            if (File.Exists(output) && (await File.ReadAllBytesAsync(output, cancellationToken)).AsSpan().SequenceEqual(data)) continue;
            var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, data, cancellationToken);
                File.Move(temporary, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            Console.WriteLine($"Prepared {language}: {index.Count} trigrams, {data.Length} bytes.");
        }
        return 0;
    }
}
