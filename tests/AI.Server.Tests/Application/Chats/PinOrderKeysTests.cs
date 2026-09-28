namespace AI.Application.Tests.Chats;

using AI.Application.Chats;
using Shouldly;
using Xunit;

public class PinOrderKeysTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("V", null)]
    [InlineData(null, "V")]
    [InlineData("V", "W")]
    [InlineData(null, "1")]
    [InlineData("V1", "V2")]
    [InlineData("VV", "W")]
    [InlineData("zz", null)]
    public void ShouldMakeKeyStrictlyBetweenBounds(string? lower, string? upper)
    {
        var key = CreateInstance().Between(lower, upper);

        if (lower is not null) string.CompareOrdinal(lower, key).ShouldBeLessThan(0);
        if (upper is not null) string.CompareOrdinal(key, upper).ShouldBeLessThan(0);
        key.ShouldNotEndWith("0");
    }

    [Fact]
    public void ShouldKeepMakingRoomWhenInsertingRepeatedlyIntoTheSameGap()
    {
        var keys = CreateInstance();
        var lower = keys.Between(null, null);
        var upper = keys.Between(lower, null);
        for (var step = 0; step < 200; step++)
        {
            var key = keys.Between(lower, upper);
            string.CompareOrdinal(lower, key).ShouldBeLessThan(0);
            string.CompareOrdinal(key, upper).ShouldBeLessThan(0);
            upper = key;
        }
    }

    [Fact]
    public void ShouldRejectDescendingRange() =>
        Should.Throw<ArgumentException>(() => CreateInstance().Between("W", "V"));

    private static PinOrderKeys CreateInstance() => new();
}
