namespace AI.Client.Infrastructure.Logging;

using Microsoft.Extensions.Logging;
using System.Text.Json;

public sealed class JsonLineFileLoggerProvider : ILoggerProvider
{
    private readonly Lock _sync = new();
    private readonly string _logsDirectory;
    private readonly int _retentionDays;

    public JsonLineFileLoggerProvider(string rootDirectory, int retentionDays = 14)
    {
        _logsDirectory = Path.Combine(rootDirectory, "logs");
        _retentionDays = retentionDays;
        Directory.CreateDirectory(_logsDirectory);
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
                File.AppendAllText(path, entry + Environment.NewLine);
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
        foreach (var path in Directory.EnumerateFiles(_logsDirectory, "ai-client-*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(path) < threshold)
            {
                File.Delete(path);
            }
        }
    }

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
