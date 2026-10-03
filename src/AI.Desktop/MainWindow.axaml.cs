namespace AI.Desktop;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Text.Json;

internal sealed partial class MainWindow : Window
{
    // Long enough for a cold WebView2 start on a slow disk; a missing engine never gets there.
    private static readonly TimeSpan EngineTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions UpdateJson = new(JsonSerializerDefaults.Web);
    private DesktopStart _start;
    private bool _closed;
    private readonly IWindowPlacementStore _placements;
    private readonly IWorkspaceLocationStore _workspaceLocation;
    private readonly IClientSettingsStore _clientSettings;
    private readonly ITaskbarBadge _taskbarBadge;
    private readonly IFileDropBridge _fileDrop;
    private readonly IDesktopUpdates _updates;
    private readonly DispatcherTimer _engineWatchdog;
    private bool _engineCreated;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    /// <summary>The page picked a theme: "system", "light", "dark", "darkblue", "gray" or "lightgray".</summary>
    public event Action<string>? ThemeRequested;

    public MainWindow(DesktopStart start, IWindowPlacementStore placements,
        IWorkspaceLocationStore workspaceLocation, IClientSettingsStore clientSettings,
        ITaskbarBadge taskbarBadge, IFileDropBridge fileDrop, IDesktopUpdates updates)
    {
        _start = start;
        _placements = placements;
        _workspaceLocation = workspaceLocation;
        _clientSettings = clientSettings;
        _taskbarBadge = taskbarBadge;
        _fileDrop = fileDrop;
        _updates = updates;
        _updates.ShutdownRequested += () => Dispatcher.UIThread.Post(Close);
        InitializeComponent();
        Restore(placements.Load());
        PositionChanged += (_, _) => RememberNormalLater();
        _engineWatchdog = new DispatcherTimer { Interval = EngineTimeout };
        _engineWatchdog.Tick += (_, _) => OnEngineTimeout();
        WebView.EnvironmentRequested += (_, args) => ConfigureEnvironment(args);
        WebView.AdapterCreated += (_, _) =>
        {
            _engineCreated = true;
            _fileDrop.Attach(WebView.TryGetPlatformHandle(), OnFilesDropped);
        };
        WebView.NavigationStarted += (_, args) => KeepNavigationInApp(args);
        WebView.NewWindowRequested += (_, args) => OpenNewWindowOutside(args);
        WebView.NavigationCompleted += (_, args) => OnNavigationCompleted(args);
        WebView.WebMessageReceived += (_, args) => OnWebMessageReceived(args);
        Retry.Click += (_, _) => Load();
        UpdateHost.IsVisible = start.HostUpdateAddress is not null;
        UpdateHost.Click += async (_, _) => await UpdateIncompatibleHostAsync();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                UpdateFrame();
            }
        };
        UpdateFrame();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (OperatingSystem.IsWindows()) _taskbarBadge.Attach(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        if (_start.Address is null)
        {
            ShowProblem("The AI Client server did not start.", _start.Error, canRetry: false);
            return;
        }

        Load();
        _updates.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty)
        {
            RememberNormalLater();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _taskbarBadge.Detach();
        base.OnClosing(e);
        RememberNormal();
        _placements.Save(new WindowPlacement(_normalPosition.X, _normalPosition.Y, _normalSize.Width, _normalSize.Height,
            WindowState is WindowState.Maximized or WindowState.FullScreen));
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _engineWatchdog.Stop();
        _fileDrop.Detach();
        base.OnClosed(e);
    }

    /// <summary>
    /// Brings back the last placement, unless no screen shows its titlebar any more (a monitor was
    /// unplugged or the layout changed): then the window opens centered as on the first run.
    /// </summary>
    private void Restore(WindowPlacement? placement)
    {
        _normalSize = new Size(Width, Height);
        if (placement is null)
        {
            return;
        }

        var titlebar = new PixelRect(placement.X, placement.Y, (int)placement.Width, 38);
        var screens = Screens?.All ?? [];
        if (screens.Any(screen => screen.WorkingArea.Intersect(titlebar) is { Width: >= 100, Height: >= 19 }))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = _normalPosition = new PixelPoint(placement.X, placement.Y);
            Width = placement.Width;
            Height = placement.Height;
            _normalSize = new Size(Width, Height);
        }

        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Maximizing moves and resizes the window before its state says so, so the bounds are read
    /// once the state has caught up.
    /// </summary>
    private void RememberNormalLater() => Dispatcher.UIThread.Post(RememberNormal, DispatcherPriority.Background);

    /// <summary>Only a normal window's bounds are worth restoring; a maximized one's come from its screen.</summary>
    private void RememberNormal()
    {
        if (WindowState == WindowState.Normal && IsVisible)
        {
            _normalPosition = Position;
            _normalSize = ClientSize;
        }
    }

    private void Load()
    {
        Status.IsVisible = false;
        WebView.IsVisible = true;
        _engineWatchdog.Start();
        WebView.Navigate(_workspaceLocation.Restore(_start.Address!) ?? _start.Address!);
    }

    /// <summary>
    /// Every engine keeps its profile under the data directory: next to the executable is not
    /// writable once the app is installed, and a per-directory profile keeps two data directories
    /// from sharing one browser storage.
    /// </summary>
    private void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = _start.DevTools;
        var profile = Path.Combine(_start.DataDirectory, "webview");
        switch (args)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs webView2:
                webView2.UserDataFolder = profile;
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.BaseDataDirectory = Path.Combine(profile, "data");
                gtk.BaseCacheDirectory = Path.Combine(profile, "cache");
                // WebKitGTK embeds only through X11; under Wayland this uses XWayland instead of failing.
                gtk.ForceX11GdkBackend = true;
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                wpe.DataDirectory = Path.Combine(profile, "data");
                wpe.CacheDirectory = Path.Combine(profile, "cache");
                break;
        }
    }

    /// <summary>The window shows this app only; any other page opens in the user's browser.</summary>
    private void KeepNavigationInApp(WebViewNavigationStartingEventArgs args)
    {
        if (args.Request is not { } target || IsApp(target))
        {
            return;
        }

        args.Cancel = true;
        OpenOutside(target);
    }

    private void OpenNewWindowOutside(WebViewNewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (args.Request is { } target)
        {
            OpenOutside(target);
        }
    }

    private void OnNavigationCompleted(WebViewNavigationCompletedEventArgs args)
    {
        _engineWatchdog.Stop();
        if (!args.IsSuccess && args.Request is { } target && IsApp(target))
        {
            ShowProblem("The AI Client window could not load.", $"Nothing answered at {target}.", canRetry: true);
        }
    }

    private void OnWebMessageReceived(WebMessageReceivedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Body)) return;
        try
        {
            using var message = JsonDocument.Parse(args.Body);
            var root = message.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            if (type.GetString() == "update-request" && root.TryGetProperty("id", out var requestId)
                && requestId.TryGetInt32(out var id) && root.TryGetProperty("operation", out var operation))
            {
                _ = HandleUpdateAsync(id, operation.GetString() ?? "", root.TryGetProperty("preferences", out var prefs)
                    ? prefs.Deserialize<AI.Contracts.Updates.UpdatePreferences>(UpdateJson) : null);
                return;
            }
            if (type.GetString() == "open-external-link"
                && root.TryGetProperty("url", out var link) && link.ValueKind == JsonValueKind.String
                && Uri.TryCreate(link.GetString(), UriKind.Absolute, out var external))
            {
                OpenOutside(external);
            }
            else if (type.GetString() == "workspace-location")
            {
                Guid? ReadId(string name) => root.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)
                        ? id : null;
                _workspaceLocation.Save(ReadId("project"), ReadId("chat"), ReadId("branch"));
            }
            else if (type.GetString() == "client-settings-request")
            {
                var saved = _clientSettings.Load();
                WebView.InvokeScript($"window.aiClientTheme.restoreClientSettings({JsonSerializer.Serialize(saved)});");
            }
            else if (type.GetString() == "client-settings-save"
                     && root.TryGetProperty("settings", out var settings)
                     && settings.ValueKind == JsonValueKind.String
                     && settings.GetString() is { } json)
            {
                _clientSettings.Save(json);
            }
            else if (OperatingSystem.IsWindows() && type.GetString() == "unread-count"
                     && root.TryGetProperty("count", out var value) && value.ValueKind == JsonValueKind.Number
                     && value.TryGetInt32(out var count) && count is >= 0 and <= 100)
            {
                Dispatcher.UIThread.Post(() => _taskbarBadge.SetCount(count));
            }
            else if (type.GetString() == "theme"
                     && root.TryGetProperty("preference", out var preference)
                     && preference.ValueKind == JsonValueKind.String
                     && preference.GetString() is "system" or "light" or "dark" or "darkblue" or "gray" or "lightgray")
            {
                var requested = preference.GetString()!;
                Dispatcher.UIThread.Post(() => ThemeRequested?.Invoke(requested));
            }
        }
        catch (JsonException)
        {
            // Only the app's small typed bridge message is handled here.
        }
    }

    private async Task HandleUpdateAsync(int id, string operation, AI.Contracts.Updates.UpdatePreferences? preferences)
    {
        object response;
        try
        {
            response = operation == "state" ? _updates.Manager.State
                : await _updates.Manager.ExecuteAsync(operation, preferences, CancellationToken.None);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            response = new { error = error.Message };
        }
        var json = JsonSerializer.Serialize(response, UpdateJson);
        await Dispatcher.UIThread.InvokeAsync(() => WebView.InvokeScript($"window.aiClientUpdates.receive({id}, {json});"));
    }

    private async Task UpdateIncompatibleHostAsync()
    {
        UpdateHost.IsEnabled = false;
        try
        {
            using var http = new HttpClient { BaseAddress = _start.HostUpdateAddress, Timeout = TimeSpan.FromMinutes(30) };
            using var check = await http.PostAsync("api/updates/check", null);
            if (check.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ShowProblem("Update AI Client Host", "This Host predates automatic updates. Install a newer Host once, then reopen Desktop.", false);
                await Launcher.LaunchUriAsync(new Uri("https://github.com/DevTeam/AI.Client/releases"));
                return;
            }
            check.EnsureSuccessStatusCode();
            var state = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<AI.Contracts.Updates.UpdateState>(check.Content);
            if (state is null || state.Error is not null || state.Release is null)
                throw new InvalidOperationException(state?.Error ?? "No newer Host is available. Check for a newer Desktop release.");
            using var install = await http.PostAsync("api/updates/install", null);
            install.EnsureSuccessStatusCode();
            state = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<AI.Contracts.Updates.UpdateState>(install.Content);
            if (state?.Error is not null) throw new InvalidOperationException(state.Error);
            ShowProblem("Host update scheduled", "Waiting for its tasks to finish. Your workspace will open automatically after the update.", false);
            while (!_closed)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                try
                {
                    using var session = await http.GetAsync("api/bridge/session");
                    if (!session.IsSuccessStatusCode) continue;
                    var json = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<JsonElement>(session.Content);
                    if (json.TryGetProperty("productName", out var product) && product.GetString() == AI.Contracts.HostProtocol.ProductName
                        && json.TryGetProperty("apiVersion", out var api) && api.GetInt32() == AI.Contracts.HostProtocol.ApiVersion)
                    {
                        _start = _start with { Address = _start.HostUpdateAddress, Error = null, HostUpdateAddress = null };
                        UpdateHost.IsVisible = false;
                        Load();
                        _updates.Start();
                        break;
                    }
                    using var status = await http.GetAsync("api/updates");
                    if (status.IsSuccessStatusCode)
                    {
                        var update = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<AI.Contracts.Updates.UpdateState>(status.Content);
                        if (update?.Phase == AI.Contracts.Updates.UpdatePhase.Failed) throw new InvalidOperationException(update.Error);
                    }
                }
                catch (HttpRequestException) { /* The Host is between shutdown and restart. */ }
            }
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            ShowProblem("The Host could not be updated", error.Message, false);
        }
        finally { UpdateHost.IsEnabled = true; }
    }

    /// <summary>An empty list still answers the page, which then explains why nothing was added.</summary>
    private void OnFilesDropped(IReadOnlyList<string> paths) => Dispatcher.UIThread.Post(() =>
        WebView.InvokeScript(
            $"window.dispatchEvent(new CustomEvent('ai-client-files-dropped', {{ detail: {JsonSerializer.Serialize(paths)} }}));"));

    private void OnEngineTimeout()
    {
        _engineWatchdog.Stop();
        if (_engineCreated)
        {
            return;
        }

        ShowProblem("The system web view is not available.", EngineHint(), canRetry: true);
    }

    private bool IsApp(Uri target) =>
        target.Scheme is "about" or "data" || Uri.Compare(target, _start.Address, UriComponents.SchemeAndServer,
            UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private void OpenOutside(Uri target)
    {
        // Only web links leave the app; a file: or custom-scheme link from model output must not
        // launch whatever the system associates with it.
        if (target.Scheme is "http" or "https" or "mailto")
        {
            _ = Launcher.LaunchUriAsync(target);
        }
    }

    private void ShowProblem(string title, string? detail, bool canRetry)
    {
        WebView.IsVisible = false;
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
        StatusDetail.IsVisible = !string.IsNullOrWhiteSpace(detail);
        Retry.IsVisible = canRetry;
        Status.IsVisible = true;
    }

    private static string EngineHint() =>
        OperatingSystem.IsWindows()
            ? "Install the Microsoft Edge WebView2 Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and start AI Client again."
            : OperatingSystem.IsLinux()
                ? "Install WebKitGTK (for example libwebkit2gtk-4.1-0) or WPE WebKit and start AI Client again."
                : "The system web view did not start. Restart AI Client; if it happens again, report it.";

    /// <summary>A maximized or full-screen window has no edge to outline.</summary>
    private void UpdateFrame()
    {
        var edge = WindowState is WindowState.Maximized or WindowState.FullScreen ? 0 : 1;
        Frame.BorderThickness = new Thickness(edge);
        Chrome.Margin = new Thickness(edge);
    }
}
