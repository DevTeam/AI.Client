namespace AI.Infrastructure.Chat;

using System.Globalization;
using System.Text.RegularExpressions;
using AI.Contracts.Usage;

/// <summary>Reads the limits an endpoint states in its response headers.</summary>
public interface IRateLimitHeaderReader
{
    /// <summary>The limits the headers state, or null when they state none.</summary>
    RateLimitStatus? Read(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, DateTimeOffset now);
}

/// <summary>
/// Understands the spellings in use: OpenAI's <c>x-ratelimit-*-requests</c> and
/// <c>*-tokens</c> with resets like <c>6m0s</c>, Anthropic's <c>anthropic-ratelimit-*</c> with
/// timestamps, and the single-window <c>x-ratelimit-*</c> and <c>ratelimit-*</c> forms that
/// gateways send, whose reset is seconds, or a Unix time in seconds or milliseconds.
/// </summary>
public sealed partial class RateLimitHeaderReader : IRateLimitHeaderReader
{
    public RateLimitStatus? Read(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var values = headers.ToDictionary(header => header.Key.ToLowerInvariant(),
            header => header.Value.FirstOrDefault()?.Trim(), StringComparer.Ordinal);
        RateLimitWindow? Window(string limit, string remaining, string reset)
        {
            var window = new RateLimitWindow(Number(values.GetValueOrDefault(limit)),
                Number(values.GetValueOrDefault(remaining)), Reset(values.GetValueOrDefault(reset), now));
            return window is { Limit: null, Remaining: null } ? null : window;
        }

        var requests = Window("x-ratelimit-limit-requests", "x-ratelimit-remaining-requests", "x-ratelimit-reset-requests")
                       ?? Window("anthropic-ratelimit-requests-limit", "anthropic-ratelimit-requests-remaining",
                           "anthropic-ratelimit-requests-reset")
                       ?? Window("x-ratelimit-limit", "x-ratelimit-remaining", "x-ratelimit-reset")
                       ?? Window("ratelimit-limit", "ratelimit-remaining", "ratelimit-reset");
        var tokens = Window("x-ratelimit-limit-tokens", "x-ratelimit-remaining-tokens", "x-ratelimit-reset-tokens")
                     ?? Window("anthropic-ratelimit-tokens-limit", "anthropic-ratelimit-tokens-remaining",
                         "anthropic-ratelimit-tokens-reset")
                     ?? Window("anthropic-ratelimit-input-tokens-limit", "anthropic-ratelimit-input-tokens-remaining",
                         "anthropic-ratelimit-input-tokens-reset");
        return requests is null && tokens is null ? null : new RateLimitStatus(now, requests, tokens);
    }

    private static long? Number(string? value) =>
        value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && number >= 0
            ? (long)number
            : null;

    private static DateTimeOffset? Reset(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0)
            return number switch
            {
                > 100_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds((long)number),
                > 1_000_000_000 => DateTimeOffset.FromUnixTimeSeconds((long)number),
                _ => now.AddSeconds(number)
            };
        if (DurationPattern().Match(value) is { Success: true } duration)
        {
            double Part(string name) => duration.Groups[name].Success
                ? double.Parse(duration.Groups[name].Value, CultureInfo.InvariantCulture)
                : 0;
            return now + TimeSpan.FromHours(Part("h")) + TimeSpan.FromMinutes(Part("m"))
                   + TimeSpan.FromSeconds(Part("s")) + TimeSpan.FromMilliseconds(Part("ms"));
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
    }

    [GeneratedRegex(@"^(?:(?<h>\d+(?:\.\d+)?)h)?(?:(?<m>\d+(?:\.\d+)?)m(?!s))?(?:(?<s>\d+(?:\.\d+)?)s)?(?:(?<ms>\d+(?:\.\d+)?)ms)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
