namespace AI.Server.Hosting;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AI.Application.Settings;
using AI.Application.Users;
using AI.Contracts.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public interface ILocalModelService : IHostedService
{
    LocalModelStatus Status { get; }
}

/// <summary>Owns the optional installation-local inference process, never a system daemon.</summary>
public sealed partial class LocalModelService(LocalModelOptions options, IGlobalSettingsService settings, IUserDirectory users,
    IUserScope scope, ILogger<LocalModelService> logger, ILocalModelPackageLocator packages) : BackgroundService, ILocalModelService
{
    public static readonly Guid ConnectionId = LocalModelStatus.ConnectionId;

    private volatile LocalModelStatus _status = new("notInstalled");
    public LocalModelStatus Status => _status;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var directory = await packages.FindAsync(options.Directory, options.DevelopmentBaseDirectory,
                System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, stoppingToken);
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                await DisableMissingConnectionAsync(stoppingToken);
                return;
            }
            _status = new("verifying");
            var manifest = JsonSerializer.Deserialize<LocalModelManifest>(await File.ReadAllTextAsync(manifestPath, stoppingToken))
                ?? throw new InvalidDataException("The local model manifest is empty.");
            var executable = PackageFile(directory, manifest.Executable);
            var model = PackageFile(directory, manifest.Model);
            if (manifest.Port is < 1024 or > 65535 || manifest.ContextTokens is < 1024 or > 32768)
                throw new InvalidDataException("Invalid local model port or context size.");
            await using (var input = File.OpenRead(model))
            {
                if (input.Length >= 2_000_000_000) throw new InvalidDataException("Local model weights must be smaller than 2 GB.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, stoppingToken));
                if (!hash.Equals(manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Local model checksum mismatch.");
            }
            var baseUrl = $"http://127.0.0.1:{manifest.Port}/v1";
            await RegisterAsync(manifest, baseUrl, null, stoppingToken);
            var failures = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!File.Exists(manifestPath))
                {
                    await DisableMissingConnectionAsync(stoppingToken);
                    _status = new("notInstalled");
                    return;
                }
                if (!await IsEnabledAsync(stoppingToken))
                {
                    _status = new("disabled", manifest.ModelAlias);
                    failures = 0;
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                try
                {
                    await RunAsync(manifest, executable, model, baseUrl, directory, stoppingToken);
                    failures = 0;
                }
                catch (Exception error) when (IsComponentFailure(error) && !stoppingToken.IsCancellationRequested)
                {
                    Failed(logger, error);
                    _status = new("failed", manifest.ModelAlias, "Local inference stopped. Check the application log.");
                    if (++failures >= 3) return;
                    await Task.Delay(TimeSpan.FromSeconds(3 * failures), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error) when (IsComponentFailure(error))
        {
            Failed(logger, error);
            _status = new("failed", Error: "The local AI package could not be loaded. Check the application log.");
        }
        finally
        {
            if (stoppingToken.IsCancellationRequested) _status = new("stopped");
        }
    }

    private async Task RunAsync(LocalModelManifest manifest, string executable, string model, string baseUrl, string packageDirectory,
        CancellationToken stoppingToken)
    {
        Process? process = null;
        Task stdoutDrain = Task.CompletedTask;
        Task stderrDrain = Task.CompletedTask;
        try
        {
            _status = new("starting", manifest.ModelAlias);
            // A separate key for each launch prevents accidentally trusting another process on this port.
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)!,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "--model", model, "--alias", manifest.ModelAlias, "--host", "127.0.0.1",
                         "--port", manifest.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         "--ctx-size", manifest.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         "--parallel", "1", "--jinja",
                         "--api-key", key }) start.ArgumentList.Add(argument);
            if (manifest.DisableThinking)
            {
                start.ArgumentList.Add("--chat-template-kwargs");
                start.ArgumentList.Add("{\"enable_thinking\":false}");
            }
            process = Process.Start(start) ?? throw new InvalidOperationException("Local inference did not start.");
            // A child must not write into the Host's CLI/MCP/test protocol streams or fill its pipes.
            stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, stoppingToken);
            stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, stoppingToken);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", key);
            using var readiness = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            readiness.CancelAfter(TimeSpan.FromMinutes(2));
            while (true)
            {
                readiness.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("Local inference exited before becoming ready.");
                try
                {
                    using var response = await http.GetAsync(baseUrl + "/models", readiness.Token);
                    if (response.IsSuccessStatusCode) break;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!readiness.IsCancellationRequested) { }
                await Task.Delay(250, readiness.Token);
            }
            await RegisterAsync(manifest, baseUrl, key, stoppingToken);
            Ready(logger, manifest.ContextTokens);
            _status = new("ready", manifest.ModelAlias);
            while (!stoppingToken.IsCancellationRequested && await IsEnabledAsync(stoppingToken))
            {
                if (!File.Exists(Path.Combine(packageDirectory, "manifest.json")))
                {
                    await DisableMissingConnectionAsync(stoppingToken);
                    return;
                }
                if (process.HasExited) throw new InvalidOperationException("Local inference exited unexpectedly.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
                try { await Task.WhenAll(stdoutDrain, stderrDrain); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            }
        }
    }

    private async Task<bool> IsEnabledAsync(CancellationToken token)
    {
        foreach (var user in await users.ListAsync(token))
        {
            using var userScope = scope.Enter(user);
            if ((await settings.GetAsync(token)).Connections.Any(item => item.Id == ConnectionId && item.Enabled)) return true;
        }
        return false;
    }

    private async Task DisableMissingConnectionAsync(CancellationToken token)
    {
        foreach (var user in await users.ListAsync(token))
        {
            using var userScope = scope.Enter(user);
            var connection = (await settings.GetAsync(token)).Connections.FirstOrDefault(item => item.Id == ConnectionId);
            if (connection is null) continue;
            if (connection.Enabled)
                await settings.UpsertConnectionAsync(connection with { Enabled = false }, connection, token);
            await settings.SetConnectionCredentialAsync(ConnectionId, null, token);
        }
    }

    private async Task RegisterAsync(LocalModelManifest manifest, string baseUrl, string? key, CancellationToken stoppingToken)
    {
        foreach (var user in await users.ListAsync(stoppingToken))
        {
            using var userScope = scope.Enter(user);
            var current = await settings.GetAsync(stoppingToken);
            var existing = current.Connections.FirstOrDefault(item => item.Id == ConnectionId);
            // Preserve user choices, including disabling the connection and selecting another default.
            var connection = existing is null
                ? new ConnectionSettings(ConnectionId, manifest.ConnectionName, baseUrl, manifest.ModelAlias,
                    true, false, false, ContextWindowTokens: manifest.ContextTokens,
                    ReservedOutputTokens: 1024, ImageInput: ImageInputMode.Disabled)
                : existing with { BaseUrl = baseUrl, Model = manifest.ModelAlias, ContextWindowTokens = manifest.ContextTokens,
                    // A name the application gave follows the installed model; a name the user chose stays.
                    Name = existing.Name is "Local Qwen3 0.6B" or "Local Qwen3.5 2B" or "Local MNN-Pocket 0.3B Q4_1 QAT"
                        ? manifest.ConnectionName : existing.Name };
            if (connection != existing) await settings.UpsertConnectionAsync(connection, existing, stoppingToken);
            if (key is not null) await settings.SetConnectionCredentialAsync(ConnectionId, key, stoppingToken);
        }
    }

    private static bool IsComponentFailure(Exception error) => error is IOException or InvalidDataException
        or UnauthorizedAccessException or InvalidOperationException or JsonException
        or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException;
    private static string PackageFile(string directory, string relative)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !File.Exists(path)) throw new InvalidDataException("Missing or invalid local model package file.");
        return path;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local inference is ready with a {ContextTokens} token context.")]
    private static partial void Ready(ILogger logger, int contextTokens);

    [LoggerMessage(Level = LogLevel.Error, Message = "The optional local model is unavailable. Other connections remain available.")]
    private static partial void Failed(ILogger logger, Exception error);
}

public sealed record LocalModelManifest(string Executable, string Model, string ModelSha256,
    int Port = 52176, int ContextTokens = 8192, string ModelAlias = "qwen3.5-2b-local",
    string ConnectionName = "Local Qwen3.5 2B", bool DisableThinking = true);

public sealed record LocalModelOptions(string Directory, string? DevelopmentBaseDirectory = null);

