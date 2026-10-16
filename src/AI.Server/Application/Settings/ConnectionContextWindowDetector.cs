namespace AI.Application.Settings;

using System.Collections.Concurrent;
using AI.Application.Projects;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Asks the endpoint's <c>/models</c> once per connection, Base URL and model, and keeps the answer.
/// A window found for the connection's own model is also stored on the connection, so settings show
/// it and later runs, other features and restarts read it without asking. A window found for a
/// model a branch picked, and an endpoint that reports none, are kept in memory only.
/// </summary>
public sealed class ConnectionContextWindowDetector(
    IConnectionModelsResolver models,
    IGlobalSecretStore secretStore,
    IGlobalSettingsRepository settings,
    IClock clock,
    ILogger<ConnectionContextWindowDetector> logger) : IConnectionContextWindowDetector
{
    // The question is asked before the first request of a turn, so the turn waits for it only
    // briefly; a slower answer is still taken in and serves the turns after this one.
    private static readonly TimeSpan TurnWait = TimeSpan.FromSeconds(2);

    // How long the catalog may take at all before the endpoint counts as reporting nothing.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // An endpoint that reported nothing, or could not be reached, is asked again after this long:
    // often enough to notice a server restarted with a better catalog, rarely enough to cost nothing.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    private static readonly Action<ILogger, string, string, Exception?> NotReported =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1, "ContextWindowNotReported"),
            "The model catalog at {BaseUrl} could not be read for the context window of {Model}.");

    private static readonly Action<ILogger, string, Exception?> NotStored =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2, "ContextWindowNotStored"),
            "Could not store the detected context window of {Model}.");

    private readonly ConcurrentDictionary<Key, Lazy<Task<Detection>>> _detections = new();

    public async Task<ConnectionSettings> DetectAsync(ConnectionSettings connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.ContextWindowTokens is not null || string.IsNullOrWhiteSpace(connection.Model)
            || connection.DetectedContextWindow?.Model == connection.Model)
            return connection;

        var key = new Key(connection.Id, connection.BaseUrl.TrimEnd('/'), connection.Model);
        Detection detection;
        try
        {
            detection = await DetectionAsync(key).WaitAsync(TurnWait, cancellationToken);
        }
        catch (TimeoutException)
        {
            return connection;
        }

        if (detection.Tokens is not { } tokens) return connection;

        var detected = new DetectedContextWindow(connection.Model, tokens);
        if (detection.Persist) await PersistAsync(key, detected, cancellationToken);
        return connection with { DetectedContextWindow = detected };
    }

    private Task<Detection> DetectionAsync(Key key)
    {
        while (true)
        {
            var lazy = _detections.GetOrAdd(key, item => new Lazy<Task<Detection>>(() => FetchAsync(item)));
            var task = lazy.Value;
            // A stale "not reported" is dropped and asked again; a reported window never goes stale
            // while the process runs, and one for the connection's model is stored anyway.
            if (task.IsCompletedSuccessfully && task.Result is { Tokens: null } miss
                && clock.UtcNow - miss.At >= RetryAfter)
            {
                _detections.TryRemove(new KeyValuePair<Key, Lazy<Task<Detection>>>(key, lazy));
                continue;
            }

            return task;
        }
    }

    private async Task<Detection> FetchAsync(Key key)
    {
        // Shared by every turn that asks at once, so it runs on its own clock rather than on the
        // token of whichever turn happened to ask first.
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            var apiKey = await secretStore.GetAsync("connection", key.ConnectionId, timeout.Token);
            var catalog = await models.ResolveAsync(key.BaseUrl, apiKey, timeout.Token);
            var model = catalog.FirstOrDefault(item => string.Equals(item.Id, key.Model, StringComparison.Ordinal))
                ?? catalog.FirstOrDefault(item => string.Equals(item.Id, key.Model, StringComparison.OrdinalIgnoreCase));
            return new Detection(model?.ContextWindowTokens, clock.UtcNow, true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            NotReported(logger, key.BaseUrl, key.Model, error);
            return new Detection(null, clock.UtcNow, false);
        }
    }

    private async Task PersistAsync(Key key, DetectedContextWindow detected, CancellationToken cancellationToken)
    {
        try
        {
            // A window for a model only a branch runs has no connection to go on; nothing to write.
            if ((await settings.LoadAsync(cancellationToken)).Connections.Any(item => Stores(item, key, detected)))
                await settings.UpdateAsync((stored, _) => Task.FromResult(stored with
                {
                    Connections = stored.Connections.Select(item => Stores(item, key, detected)
                        ? item with { DetectedContextWindow = detected }
                        : item).ToArray()
                }), cancellationToken);
            // Stored once (or never to be); later turns find it on the connection or in memory.
            var done = new Lazy<Task<Detection>>(Task.FromResult(new Detection(detected.Tokens, clock.UtcNow, false)));
            _detections[key] = done;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The window is still used for this turn; only remembering it across restarts failed.
            NotStored(logger, key.Model, error);
        }
    }

    private static bool Stores(ConnectionSettings item, Key key, DetectedContextWindow detected) =>
        item.Id == key.ConnectionId
        && item.ContextWindowTokens is null
        && string.Equals(item.Model, key.Model, StringComparison.Ordinal)
        && string.Equals(item.BaseUrl.TrimEnd('/'), key.BaseUrl, StringComparison.Ordinal)
        && item.DetectedContextWindow != detected;

    private sealed record Key(Guid ConnectionId, string BaseUrl, string Model);

    /// <param name="Tokens">The reported window; null when the catalog says nothing or failed.</param>
    /// <param name="At">When it was asked, for retrying a miss.</param>
    /// <param name="Persist">Whether the reported window has yet to be stored on the connection.</param>
    private sealed record Detection(long? Tokens, DateTimeOffset At, bool Persist);
}
