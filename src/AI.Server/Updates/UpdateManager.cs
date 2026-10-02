namespace AI.Updates;

using AI.Contracts.Updates;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>Owns updates for one installed product. The installer runs outside this process.</summary>
public sealed class UpdateManager : IUpdateManager
{
    private readonly HttpClient _http;
    private readonly IUpdateFeed _feed;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _directory;
    private readonly string _stateFile;
    private readonly string _resultFile;
    private readonly Func<CancellationToken, Task<bool>> _tryEnterMaintenance;
    private readonly Action _leaveMaintenance;
    private readonly Action _shutdown;
    private readonly IUpdateInstaller _installer;
    private DateTimeOffset _nextCheck = DateTimeOffset.MinValue;
    private bool _installRequested;
    private bool _maintenance;
    private FileStream? _owner;
    private volatile UpdateState _state;

    public UpdateManager(string product, string dataDirectory, HttpClient http, IUpdateFeed feed, IUpdateInstaller installer, InstalledUpdateProduct installation,
        Func<CancellationToken, Task<bool>> tryEnterMaintenance, Action leaveMaintenance, Action shutdown)
    {
        _http = http;
        _feed = feed;
        _tryEnterMaintenance = tryEnterMaintenance;
        _leaveMaintenance = leaveMaintenance;
        _shutdown = shutdown;
        var runtime = installation.Runtime;
        var version = installation.Version;
        _directory = Path.Combine(dataDirectory, "updates", product);
        _stateFile = Path.Combine(_directory, "state.json");
        _resultFile = Path.Combine(_directory, "result.json");
        _installer = installer;
        var supported = installation.Supported;
        _state = new UpdateState(product, version, runtime, new UpdatePreferences(InstallAutomatically: product == "Host"), Supported: supported);
        if (File.Exists(_stateFile))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_stateFile));
                if (saved is not null)
                {
                    _state = _state with { Preferences = saved.Preferences, LastChecked = saved.LastChecked,
                        InstalledVersion = saved.InstalledVersion == version ? saved.InstalledVersion : null,
                        FailedVersion = saved.FailedVersion, Error = saved.FailedVersion is null ? null : saved.Error };
                    if (saved.Release is { } release && UpdateVersion.Parse(release.Version)?.CompareTo(UpdateVersion.Parse(version)) > 0)
                        _state = _state with { Release = release, Phase = File.Exists(PackagePath(release)) ? UpdatePhase.Ready : UpdatePhase.Available };
                    if (_state.Release?.Version == saved.FailedVersion && saved.FailedVersion is not null)
                        _state = _state with { Phase = UpdatePhase.Failed };
                    if (saved.Phase == UpdatePhase.Installing && !File.Exists(_resultFile) && saved.Release?.Version != version)
                        _state = _state with { Error = "The previous update did not complete. You can retry.", Phase = UpdatePhase.Failed };
                }
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                _state = _state with { Error = "Update preferences could not be read. Default preferences are being used." };
            }
        }
    }

    public UpdateState State => _state;

    public async Task RunAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        try { _owner = new FileStream(Path.Combine(_directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { _state = _state with { Supported = false, Error = "Another instance manages updates for this product." }; return; }
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _gate.WaitAsync(token);
                try
                {
                    await ReadResultAsync(token);
                    if (_state.Supported && _state.Preferences.CheckAutomatically && DateTimeOffset.UtcNow >= _nextCheck)
                        await CheckCoreAsync(token);
                    if (_state.Supported && _state.Phase is not (UpdatePhase.Failed or UpdatePhase.Installing))
                    {
                        if (_state.Preferences.DownloadAutomatically && _state.Phase == UpdatePhase.Available)
                            await DownloadCoreAsync(token);
                        if ((_installRequested || _state.Preferences.InstallAutomatically) && _state.Phase is UpdatePhase.Ready or UpdatePhase.WaitingForTasks)
                            await InstallCoreAsync(token);
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    await FailAsync(error, token);
                }
                finally { _gate.Release(); }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async Task<UpdateState> ExecuteAsync(string operation, UpdatePreferences? preferences, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_state.Phase == UpdatePhase.Installing) return _state;
            if (operation == "preferences")
            {
                ArgumentNullException.ThrowIfNull(preferences);
                if (!Enum.IsDefined(preferences.Channel)) throw new ArgumentException("Unknown update channel.");
                var channelChanged = preferences.Channel != _state.Preferences.Channel;
                _state = _state with { Preferences = preferences, Error = null };
                if (channelChanged)
                {
                    _installRequested = false;
                    _state = _state with { Release = null, Phase = UpdatePhase.Idle };
                    _nextCheck = DateTimeOffset.MinValue;
                }
                if (!preferences.InstallAutomatically && _state.Phase == UpdatePhase.WaitingForTasks && !_installRequested)
                    _state = _state with { Phase = UpdatePhase.Ready };
                await SaveAsync(token);
                return _state;
            }
            if (!_state.Supported && operation != "check") throw new InvalidOperationException("Installation requires an installed release of this product.");
            switch (operation)
            {
                case "check": _state = _state with { FailedVersion = null }; await CheckCoreAsync(token); break;
                case "download": _state = _state with { FailedVersion = null }; await DownloadCoreAsync(token); break;
                case "install":
                    _state = _state with { FailedVersion = null };
                    if (_state.Phase != UpdatePhase.Ready && _state.Phase != UpdatePhase.WaitingForTasks) await DownloadCoreAsync(token);
                    _installRequested = true;
                    _state = _state with { Phase = UpdatePhase.WaitingForTasks, Error = null };
                    await SaveAsync(token);
                    break;
                default: throw new ArgumentException("Unknown update operation.");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested) { await FailAsync(error, token); }
        finally { _gate.Release(); }
        return _state;
    }

    private async Task CheckCoreAsync(CancellationToken token)
    {
        if (_state.Phase is UpdatePhase.Installing or UpdatePhase.WaitingForTasks) return;
        _nextCheck = DateTimeOffset.UtcNow.AddHours(6).AddSeconds(Random.Shared.Next(60));
        var previousError = _state.Error;
        _state = _state with { Phase = UpdatePhase.Checking, Error = null };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var (release, stable) = await _feed.FindAsync(_state.Product, _state.Runtime, _state.CurrentVersion, _state.Preferences.Channel, timeout.Token);
        _state = _state with { Release = release, StableAvailable = stable, LastChecked = DateTimeOffset.UtcNow,
            Phase = release is null ? UpdatePhase.Idle : File.Exists(PackagePath(release)) ? UpdatePhase.Ready : UpdatePhase.Available };
        if (release is not null && release.Version == _state.FailedVersion)
            _state = _state with { Phase = UpdatePhase.Failed, Error = previousError };
        else _state = _state with { FailedVersion = null };
        await SaveAsync(token);
    }

    private string PackagePath(UpdateRelease release) => Path.Combine(_directory, release.Sha256 + Path.GetExtension(release.PackageName));

    private async Task DownloadCoreAsync(CancellationToken token)
    {
        var release = _state.Release ?? throw new InvalidOperationException("Check for a new version first.");
        _state = _state with { Phase = UpdatePhase.Downloading, Error = null };
        await DownloadFileAsync(release.PackageUrl, release.Sha256, PackagePath(release), token);
        if (OperatingSystem.IsLinux() && Directory.Exists("/opt/ai-client-csharp-mcp"))
        {
            if (release.CompanionUrl is null || release.CompanionSha256 is null)
                throw new InvalidOperationException("This release has no update for the installed C# scripting tools.");
            await DownloadFileAsync(release.CompanionUrl, release.CompanionSha256, Path.Combine(_directory, "companion.deb"), token);
        }
        _state = _state with { Phase = UpdatePhase.Ready };
        await SaveAsync(token);
    }

    private async Task DownloadFileAsync(string url, string digest, string path, CancellationToken token)
    {
        if (File.Exists(path) && await VerifyAsync(path, digest, token)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using (var file = new FileStream(path + ".partial", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await response.Content.CopyToAsync(file, timeout.Token);
        if (!await VerifyAsync(path + ".partial", digest, token)) throw new InvalidOperationException("The update package failed its SHA-256 check.");
        File.Move(path + ".partial", path, true);
    }

    private static async Task<bool> VerifyAsync(string path, string digest, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, token)), digest, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallCoreAsync(CancellationToken token)
    {
        if (!_maintenance && !await _tryEnterMaintenance(token))
        {
            _state = _state with { Phase = UpdatePhase.WaitingForTasks };
            return;
        }
        _maintenance = true;
        var release = _state.Release!;
        if (!await VerifyAsync(PackagePath(release), release.Sha256, token)) throw new InvalidOperationException("The downloaded update was changed. Download it again.");
        _state = _state with { Phase = UpdatePhase.Installing, Error = null };
        await SaveAsync(token);
        // Clients poll every second and flush their pending composer writes before the exit.
        await Task.Delay(TimeSpan.FromSeconds(5), token);
        await _installer.LaunchAsync(_state, _directory, PackagePath(release), _resultFile, token);
        _shutdown();
    }

    private async Task ReadResultAsync(CancellationToken token)
    {
        if (!File.Exists(_resultFile)) return;
        try
        {
            var result = JsonSerializer.Deserialize<UpdateResult>(await File.ReadAllTextAsync(_resultFile, token));
            if (result is null) return;
            if (_maintenance) { _leaveMaintenance(); _maintenance = false; }
            var successful = result.Success && result.Version == _state.CurrentVersion;
            _state = _state with { InstalledVersion = successful ? result.Version : null,
                Error = successful ? null : "The update did not complete. See the installer log in the updates directory.",
                Phase = successful ? UpdatePhase.Idle : UpdatePhase.Failed, Release = successful ? null : _state.Release,
                FailedVersion = successful ? null : result.Version };
            await SaveAsync(token);
            File.Delete(_resultFile);
        }
        catch (JsonException) { /* The helper may still be writing its result. */ }
    }

    private async Task FailAsync(Exception error, CancellationToken token)
    {
        if (_maintenance) { _leaveMaintenance(); _maintenance = false; }
        _installRequested = false;
        _state = _state with { Phase = UpdatePhase.Failed, FailedVersion = _state.Release?.Version,
            Error = error is OperationCanceledException ? "The update request timed out. Try again." : error.Message };
        await SaveAsync(token);
    }

    private async Task SaveAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_stateFile + ".tmp", JsonSerializer.Serialize(_state), token);
        File.Move(_stateFile + ".tmp", _stateFile, true);
    }

    public ValueTask DisposeAsync()
    {
        _owner?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    public sealed record UpdateResult(bool Success, string Version);
}
