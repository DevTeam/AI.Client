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

    /// <summary>
    /// Starts the runtime again after it stopped on repeated failures. A damaged package cannot be
    /// helped by trying again, so it is not retried; nor is a runtime that is not in the failed state.
    /// </summary>
    bool Retry();
}

/// <summary>Owns the optional installation-local inference process, never a system daemon.</summary>
public sealed partial class LocalModelService(LocalModelOptions options, IGlobalSettingsService settings, IUserDirectory users,
    IUserScope scope, ILogger<LocalModelService> logger, ILocalModelPackageLocator packages) : BackgroundService, ILocalModelService
{
    public static readonly Guid ConnectionId = LocalModelStatus.ConnectionId;

    private const string Damaged = "The Local AI files are damaged. Reinstall AI Client and keep the Local AI component selected.";
    private const string Incomplete = "The Local AI component is incomplete. Reinstall AI Client and keep the Local AI component selected.";

    private volatile LocalModelStatus _status = new("notInstalled");
    private readonly SemaphoreSlim _retry = new(0, 1);
    private volatile bool _retryable;
    public LocalModelStatus Status => _retryable && _status.State == "failed" ? _status with { CanRetry = true } : _status;

    public bool Retry()
    {
        if (!_retryable || _status.State != "failed" || _retry.CurrentCount > 0) return false;
        _status = _status with { State = "starting", Error = null };
        _retry.Release();
        return true;
    }

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
            LocalModelManifest manifest;
            string executable, model;
            try
            {
                manifest = JsonSerializer.Deserialize<LocalModelManifest>(await File.ReadAllTextAsync(manifestPath, stoppingToken))
                    ?? throw new InvalidDataException("The local model manifest is empty.");
                executable = PackageFile(directory, manifest.Executable);
                model = PackageFile(directory, manifest.Model);
                if (manifest.Port is < 1024 or > 65535 || manifest.ContextTokens is < 1024 or > 32768)
                    throw new InvalidDataException("Invalid local model port or context size.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                Failed(logger, error);
                _status = new("failed", Error: Incomplete);
                return;
            }
            _status = Describe(manifest, "verifying");
            await using (var input = File.OpenRead(model))
            {
                var hash = input.Length < 2_000_000_000
                    ? Convert.ToHexString(await SHA256.HashDataAsync(input, stoppingToken)) : null;
                if (hash is null || !hash.Equals(manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Failed(logger, new InvalidDataException("Local model checksum mismatch or oversized weights."));
                    _status = Describe(manifest, "failed", Damaged);
                    return;
                }
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
                    _status = Describe(manifest, "disabled");
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
                    var reason = error is LocalModelStartException start ? start.Message
                        : "The local model stopped unexpectedly. Other connections keep working.";
                    _status = Describe(manifest, "failed", reason);
                    // A busy port or a blocked runtime will not clear up by itself within seconds.
                    if (++failures >= 3 || error is LocalModelStartException { Final: true })
                    {
                        _retryable = true;
                        await _retry.WaitAsync(stoppingToken);
                        _retryable = false;
                        failures = 0;
                        continue;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(3 * failures), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error) when (IsComponentFailure(error))
        {
            Failed(logger, error);
            _status = new("failed", Error: "Local AI could not be loaded. Other connections keep working.");
        }
        finally
        {
            if (stoppingToken.IsCancellationRequested) _status = new("stopped");
        }
    }

    private static LocalModelStatus Describe(LocalModelManifest manifest, string state, string? error = null) =>
        new(state, manifest.ModelAlias, error, manifest.ConnectionName, manifest.ContextTokens);

    private async Task RunAsync(LocalModelManifest manifest, string executable, string model, string baseUrl, string packageDirectory,
        CancellationToken stoppingToken)
    {
        Process? process = null;
        Task stdoutDrain = Task.CompletedTask;
        Task stderrDrain = Task.CompletedTask;
        try
        {
            _status = Describe(manifest, "starting");
            if (PortInUse(manifest.Port))
                throw new LocalModelStartException($"Port {manifest.Port} is used by another program, so the local model cannot start. "
                    + "Close the other AI Client window or the program that uses it, then try again.", final: true);
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
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("Local inference did not start.");
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                throw new LocalModelStartException("The local model runtime could not be started. Security software may have "
                    + "blocked it; allow AI Client's local-ai folder, then try again.", final: true, error);
            }
            // A child must not write into the Host's CLI/MCP/test protocol streams or fill its pipes.
            stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, stoppingToken);
            stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, stoppingToken);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", key);
            using var readiness = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            readiness.CancelAfter(TimeSpan.FromMinutes(2));
            while (true)
            {
                if (readiness.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                    throw new LocalModelStartException("The local model did not finish loading within two minutes. "
                        + "Close other programs to free memory, then try again.");
                stoppingToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new LocalModelStartException("The local model stopped while loading. This computer may not have "
                        + "enough free memory; close other programs, then try again.");
                try
                {
                    using var response = await http.GetAsync(baseUrl + "/models", readiness.Token);
                    if (response.IsSuccessStatusCode) break;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                try { await Task.Delay(250, readiness.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
            await RegisterAsync(manifest, baseUrl, key, stoppingToken);
            Ready(logger, manifest.ContextTokens);
            _status = Describe(manifest, "ready");
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

    private static bool PortInUse(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            return false;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return true;
        }
    }

    /// <summary>A start failure whose message is written for the person reading the connection's status.</summary>
    private sealed class LocalModelStartException(string message, bool final = false, Exception? inner = null)
        : InvalidOperationException(message, inner)
    {
        /// <summary>Trying again at once would fail the same way, so the service waits for a retry request.</summary>
        public bool Final { get; } = final;
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
                ? NewConnection(manifest, baseUrl, current.Connections)
                : Defaulted(existing) with { BaseUrl = baseUrl, Model = manifest.ModelAlias, ContextWindowTokens = manifest.ContextTokens,
                    // A name the application gave follows the installed model; a name the user chose stays.
                    Name = existing.Name is "Local Qwen3 0.6B" or "Local Qwen3.5 2B" or "Local MNN-Pocket 0.3B Q4_1 QAT"
                        ? manifest.ConnectionName : existing.Name };
            if (connection != existing) await settings.UpsertConnectionAsync(connection, existing, stoppingToken);
            if (key is not null) await settings.SetConnectionCredentialAsync(ConnectionId, key, stoppingToken);
        }
    }

    /// <summary>
    /// The local connection as the application first registers it. It answers on this computer, so it
    /// costs nothing; it reads no images; its profile says it is a small model, so delegated work picks
    /// it only for what a small model manages. It becomes the default only when it is the sole
    /// connection: a fresh installation can chat at once, and an existing default is never taken over.
    /// </summary>
    public static ConnectionSettings NewConnection(LocalModelManifest manifest, string baseUrl,
        IReadOnlyCollection<ConnectionSettings> others) =>
        Defaulted(new ConnectionSettings(ConnectionId, manifest.ConnectionName, baseUrl, manifest.ModelAlias,
            Enabled: true, IsDefault: others.Count == 0, HasCredential: false, ForSubtasks: false,
            ContextWindowTokens: manifest.ContextTokens, ReservedOutputTokens: 1024, ImageInput: ImageInputMode.Disabled));

    /// <summary>
    /// Fills the profile and prices of a local connection nobody has described yet, including one an
    /// earlier version registered without them. Any value already there, the user's or not, stays.
    /// </summary>
    public static ConnectionSettings Defaulted(ConnectionSettings connection) =>
        connection is { Capability: null, GoodFor: null, Prices: null }
            ? connection with
            {
                Capability = 1, GoodFor = "Offline, private, short tasks",
                Prices = new AI.Contracts.Usage.TokenPrices(0, 0, 0), ImageInput = ImageInputMode.Disabled
            }
            : connection;

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

