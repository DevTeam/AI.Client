using Durations;
using Xunit;

public class DurationParserTests
{
    [Fact]
    public void ParsesSeconds() => Assert.Equal(TimeSpan.FromSeconds(45), DurationParser.Parse("45s"));
}
