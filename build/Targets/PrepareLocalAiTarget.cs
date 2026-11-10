namespace Build.Targets;

using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>Prepares one versioned, reusable local model package without shell dependencies.</summary>
internal sealed class PrepareLocalAiTarget(IBuildPaths paths, HttpClient http) : IPrepareLocalAiTarget
{
    private const string Version = "b12244";
    private const string Model = "Qwen_Qwen3-0.6B-Q4_K_M.gguf";
    private const string ModelHash = "9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> RunAsync(string runtime, CancellationToken cancellationToken)
    {
        var archive = runtime switch
        {
            "win-x64" => "win-cpu-x64.zip", "win-arm64" => "win-cpu-arm64.zip",
            "osx-x64" => "macos-x64.tar.gz", "osx-arm64" => "macos-arm64.tar.gz",
            "linux-x64" => "ubuntu-x64.tar.gz", "linux-arm64" => "ubuntu-arm64.tar.gz",
            _ => throw new ArgumentException($"Unsupported local AI runtime: {runtime}", nameof(runtime))
        };
        var root = Path.Combine(paths.SolutionDirectory, "artifacts", "local-ai");
        Directory.CreateDirectory(root);
        // Separate build invocations may prepare the same shared weights concurrently.
        await using var gate = await LockAsync(Path.Combine(root, ".prepare.lock"), cancellationToken);
        var package = Path.Combine(root, $"qwen3-q4-k-m-{Version}", runtime);
        var executableName = runtime.StartsWith("win-", StringComparison.Ordinal) ? "llama-server.exe" : "llama-server";
        var manifestPath = Path.Combine(package, "manifest.json");
        if (File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            var data = manifest.RootElement;
            if (data.GetProperty("RuntimeVersion").GetString() == Version
                && data.GetProperty("RuntimeIdentifier").GetString() == runtime
                && File.Exists(Path.Combine(package, data.GetProperty("Executable").GetString()!))
                && await MatchesAsync(Path.Combine(package, Model), ModelHash, cancellationToken))
            {
                Console.WriteLine($"Local Qwen3 is already prepared: {package}");
                return package;
            }
            // Do not let the Host see a partial repair as an installed component.
            File.Delete(manifestPath);
        }
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        var weights = Path.Combine(downloads, Model);
        if (!await MatchesAsync(weights, ModelHash, cancellationToken))
        {
            await DownloadAsync($"https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF/resolve/main/{Model}?download=true",
                weights, cancellationToken);
            if (!await MatchesAsync(weights, ModelHash, cancellationToken))
                throw new InvalidDataException("Qwen3 checksum mismatch. Run preparation again to retry the download.");
        }
        var runtimeArchive = Path.Combine(downloads, $"llama-{Version}-{archive}");
        // The Windows artifact is pinned to the checksum used in the live desktop evaluation.
        var archiveHash = runtime == "win-x64"
            ? "318f11fadcbc3a86541ec55d9930cc76646c6b8d2ff18798c8477e9ff0dc452a" : null;
        if (!File.Exists(runtimeArchive) || (archiveHash is not null && !await MatchesAsync(runtimeArchive, archiveHash, cancellationToken)))
            await DownloadAsync($"https://github.com/ggml-org/llama.cpp/releases/download/{Version}/llama-{Version}-bin-{archive}",
                runtimeArchive, cancellationToken);
        if (archiveHash is not null && !await MatchesAsync(runtimeArchive, archiveHash, cancellationToken))
            throw new InvalidDataException("llama.cpp archive checksum mismatch.");
        Directory.CreateDirectory(package);
        var runtimeDirectory = Path.Combine(package, "runtime");
        Directory.CreateDirectory(runtimeDirectory);
        Console.WriteLine($"Extracting llama.cpp {Version} for {runtime}...");
        if (runtimeArchive.EndsWith(".zip", StringComparison.Ordinal))
            ZipFile.ExtractToDirectory(runtimeArchive, runtimeDirectory, overwriteFiles: true);
        else
        {
            await using var compressed = File.OpenRead(runtimeArchive);
            await using var tar = new GZipStream(compressed, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(tar, runtimeDirectory, overwriteFiles: true, cancellationToken);
        }
        var executable = Directory.EnumerateFiles(runtimeDirectory, executableName, SearchOption.AllDirectories).Single();
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Copy(weights, Path.Combine(package, Model), overwrite: true);
        var license = Path.Combine(downloads, "LICENSE-Qwen");
        if (!File.Exists(license))
            await DownloadAsync("https://huggingface.co/Qwen/Qwen3-0.6B/raw/main/LICENSE", license, cancellationToken);
        File.Copy(license, Path.Combine(package, "LICENSE-Model"), overwrite: true);
        var json = JsonSerializer.Serialize(new
        {
            Executable = Path.GetRelativePath(package, executable), Model, ModelSha256 = ModelHash,
            ModelAlias = "qwen3-0.6b-local", ConnectionName = "Local Qwen3 0.6B",
            Port = 52176, ContextTokens = 4096, DisableThinking = true,
            RuntimeVersion = Version, RuntimeIdentifier = runtime
        }, JsonOptions);
        await File.WriteAllTextAsync(manifestPath + ".tmp", json, cancellationToken);
        File.Move(manifestPath + ".tmp", manifestPath, overwrite: true);
        Console.WriteLine($"Local Qwen3 is prepared: {package}");
        return package;
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken token)
    {
        Console.WriteLine($"Downloading {Path.GetFileName(destination)}...");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using (var output = File.Create(destination + ".partial"))
            await response.Content.CopyToAsync(output, timeout.Token);
        File.Move(destination + ".partial", destination, overwrite: true);
    }

    private static async Task<bool> MatchesAsync(string path, string hash, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<FileStream> LockAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(200, token); }
        }
    }
}
