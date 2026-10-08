namespace AI.Infrastructure.Logging;

using AI.Contracts.FileSystem;
using Microsoft.Extensions.Logging;
using Storage;
using System.Text.Json;

public sealed class JsonLineFileLoggerProvider : ILoggerProvider
{
    private readonly Lock _sync = new();
    private readonly IFileSystem _files;
    private readonly string _logsDirectory;
    private readonly int _retentionDays;

    /// <summary>
    /// Production constructor. The container resolves <see cref="IProjectStorageLocation"/> so the
    /// logger and the data repositories agree on the root by construction rather than by accident.
    /// </summary>
    public JsonLineFileLoggerProvider(IProjectStorageLocation location, IFileSystem files, int retentionDays = 14)
        : this(location.RootDirectory, files, retentionDays)
    {
    }

    /// <summary>String-rooted constructor retained for one-off bootstrap and tests.</summary>
    public JsonLineFileLoggerProvider(string rootDirectory, IFileSystem files, int retentionDays = 14)
    {
        _files = files;
        _logsDirectory = Path.Combine(rootDirectory, "logs");
        _retentionDays = retentionDays;
        // Logging is called from synchronous code, so these two startup operations wait on the
        // contract rather than reshaping every ILogger call site into an asynchronous one.
        Wait(_files.CreateDirectoryAsync(_logsDirectory, ownerOnly: false, CancellationToken.None));
        DeleteExpiredLogs();
    }

    public ILogger CreateLogger(string categoryName) => new JsonLineFileLogger(categoryName, Write);

    public void Dispose()
    {
    }

    private void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception,
        IReadOnlyDictionary<string, object?> properties)
    {
        try
        {
            var entry = JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = level.ToString(),
                Category = category,
                EventId = eventId.Id,
                EventName = eventId.Name,
                Message = message,
                Properties = properties.ToDictionary(item => item.Key, item => Normalize(item.Value)),
                Exception = exception?.GetType().FullName,
                ExceptionMessage = exception?.Message
            });
            var path = Path.Combine(_logsDirectory, $"ai-client-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            lock (_sync)
            {
                Wait(_files.AppendTextAsync(path, entry + Environment.NewLine, CancellationToken.None));
            }
        }
        catch
        {
            // Logging must never break the application request pipeline.
        }
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            or Guid or DateTime or DateTimeOffset or TimeSpan => value,
        Enum enumValue => enumValue.ToString(),
        _ => value.ToString()
    };

    private void DeleteExpiredLogs()
    {
        var threshold = DateTime.UtcNow.Date.AddDays(-_retentionDays);
        var files = Wait(_files.ListFilesAsync(_logsDirectory, "ai-client-*.jsonl", CancellationToken.None));
        foreach (var path in files)
        {
            if (Wait(_files.GetLastWriteTimeAsync(path, CancellationToken.None)).UtcDateTime < threshold)
            {
                Wait(_files.DeleteFileAsync(path, CancellationToken.None));
            }
        }
    }

    /// <summary>
    /// The ILogger pipeline is synchronous, so the contract is awaited here instead of turning
    /// every call site asynchronous. The wait is bounded by a local append, never by the network.
    /// </summary>
    private static T Wait<T>(Task<T> operation) => operation.GetAwaiter().GetResult();

    private static void Wait(Task operation) => operation.GetAwaiter().GetResult();

    private sealed class JsonLineFileLogger(
        string category,
        Action<LogLevel, string, EventId, string, Exception?, IReadOnlyDictionary<string, object?>> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            // ReSharper disable once HeapView.PossibleBoxingAllocation
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(item => item.Key != "{OriginalFormat}").ToDictionary(item => item.Key, item => item.Value)
                : new Dictionary<string, object?>();
            write(logLevel, category, eventId, formatter(state, exception), exception, properties);
        }
    }
}
