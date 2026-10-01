namespace AI.Infrastructure.Tests.Chat;

using AI.Infrastructure.Chat;
using Shouldly;
using Xunit;

public sealed class RateLimitHeaderReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldReadOpenAiWindowsAndTheirDurations()
    {
        var limits = Read(("x-ratelimit-limit-requests", "500"), ("x-ratelimit-remaining-requests", "499"),
            ("x-ratelimit-reset-requests", "6m0s"), ("X-RateLimit-Limit-Tokens", "40000"),
            ("x-ratelimit-remaining-tokens", "39000"), ("x-ratelimit-reset-tokens", "1.5s")).ShouldNotBeNull();

        limits.Requests.ShouldBe(new(500, 499, Now.AddMinutes(6)));
        limits.Tokens.ShouldBe(new(40_000, 39_000, Now.AddSeconds(1.5)));
    }

    [Fact]
    public void ShouldReadAnthropicTimestampsAndGatewayUnixTimes()
    {
        var anthropic = Read(("anthropic-ratelimit-requests-limit", "50"), ("anthropic-ratelimit-requests-remaining", "49"),
            ("anthropic-ratelimit-requests-reset", "2026-10-01T12:01:00Z")).ShouldNotBeNull();
        var gateway = Read(("x-ratelimit-limit", "20"), ("x-ratelimit-remaining", "3"),
            ("x-ratelimit-reset", Now.AddSeconds(30).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ShouldNotBeNull();
        var ietf = Read(("ratelimit-remaining", "7"), ("ratelimit-reset", "45")).ShouldNotBeNull();

        anthropic.Requests!.ResetsAt.ShouldBe(Now.AddMinutes(1));
        gateway.Requests.ShouldBe(new(20, 3, Now.AddSeconds(30)));
        ietf.Requests.ShouldBe(new(null, 7, Now.AddSeconds(45)));
        ietf.Tokens.ShouldBeNull();
    }

    [Fact]
    public void ShouldReportNothingForAnEndpointThatStatesNoLimits() =>
        Read(("content-type", "text/event-stream")).ShouldBeNull();

    private static AI.Contracts.Usage.RateLimitStatus? Read(params (string Name, string Value)[] headers) =>
        new RateLimitHeaderReader().Read(headers.Select(header =>
            new KeyValuePair<string, IEnumerable<string>>(header.Name, [header.Value])), Now);
}
