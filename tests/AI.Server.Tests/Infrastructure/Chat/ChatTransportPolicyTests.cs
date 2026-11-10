namespace AI.Infrastructure.Tests.Chat;

using AI.Infrastructure.Chat;
using Shouldly;
using Xunit;

public sealed class ChatTransportPolicyTests
{
    [Theory]
    [InlineData(-1, 90)]
    [InlineData(0, 90)]
    [InlineData(400_000, 190)]
    [InlineData(4_000_000, 600)]
    public void HeadersShouldAllowPromptProcessing(long characters, int seconds)
    {
        new ChatTransportPolicy().ResponseHeadersTimeoutFor(characters)
            .ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void HeadersShouldPreserveExplicitOverride()
    {
        new ChatTransportPolicy(responseHeadersTimeout: TimeSpan.FromSeconds(5))
            .ResponseHeadersTimeoutFor(4_000_000).ShouldBe(TimeSpan.FromSeconds(5));
    }
}
