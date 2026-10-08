namespace AI.Infrastructure.Storage;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Application.Usage;
using AI.Contracts.FileSystem;
using AI.Contracts.Usage;

/// <summary>
/// One line per request in <c>usage/yyyy-MM.jsonl</c>. Appending keeps a write as small as the
/// record however long the month gets, and a month per file keeps a report on a recent period from
/// reading years of history. Months are read once and then held in memory, since every record
/// after that passes through here anyway.
/// </summary>
public sealed class JsonLinesTokenUsageLedger(IProjectStorageLocation location, IFileSystem files)
    : ITokenUsageLedger, IDisposable
{
    private const string MonthFormat = "yyyy-MM";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AsyncGate _gate = new();
    private readonly Dictionary<string, List<TokenUsageRecord>> _months = new(StringComparer.Ordinal);

    public void Dispose() => _gate.Dispose();

    public async Task AppendAsync(TokenUsageRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var month = Month(record.At);
        using var lease = await _gate.EnterAsync(cancellationToken);
        await files.AppendTextAsync(PathOf(month), JsonSerializer.Serialize(record, Json) + "\n", cancellationToken);
        if (_months.TryGetValue(month, out var loaded)) loaded.Add(record);
    }

    public async Task<IReadOnlyList<TokenUsageRecord>> ReadAsync(DateTimeOffset from, DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var first = Month(from);
        var last = Month(until);
        var result = new List<TokenUsageRecord>();
        foreach (var path in (await files.ListFilesAsync(UsageDirectory, "*.jsonl", cancellationToken)).Order(StringComparer.Ordinal))
        {
            var month = Path.GetFileNameWithoutExtension(path);
            if (!DateTime.TryParseExact(month, MonthFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || string.CompareOrdinal(month, first) < 0 || string.CompareOrdinal(month, last) > 0)
                continue;
            if (!_months.TryGetValue(month, out var records))
                _months[month] = records = Parse(await files.ReadTextAsync(path, cancellationToken));
            result.AddRange(records.Where(record => record.At >= from && record.At < until));
        }

        return result.OrderBy(record => record.At).ToArray();
    }

    /// <summary>
    /// A line that does not parse is one a crash cut short; the rest of the month is still good,
    /// so it is skipped rather than allowed to hide everything around it.
    /// </summary>
    private static List<TokenUsageRecord> Parse(string? text)
    {
        var records = new List<TokenUsageRecord>();
        if (text is null) return records;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (JsonSerializer.Deserialize<TokenUsageRecord>(line, Json) is { Tokens: not null } record) records.Add(record);
            }
            catch (JsonException)
            {
            }
        }

        return records;
    }

    private static string Month(DateTimeOffset at) =>
        at == DateTimeOffset.MinValue ? "0000-00"
        : at == DateTimeOffset.MaxValue ? "9999-99"
        : at.UtcDateTime.ToString(MonthFormat, CultureInfo.InvariantCulture);

    private string UsageDirectory => Path.Combine(location.RootDirectory, "usage");

    private string PathOf(string month) => Path.Combine(UsageDirectory, month + ".jsonl");
}
