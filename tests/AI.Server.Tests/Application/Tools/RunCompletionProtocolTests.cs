namespace AI.Application.Tests.Tools;

using AI.Application.Tools;
using Shouldly;
using Xunit;

public sealed class RunCompletionProtocolTests
{
    private readonly RunCompletionProtocol _protocol = new();

    [Fact]
    public void ShouldParseACompleteDefinitionOfDone()
    {
        var result = _protocol.Parse("""
            {"status":"complete","finalAnswer":"Done"}
            """);

        result.Status.ShouldBe(RunCompletionStatus.Complete);
        result.FinalAnswer.ShouldBe("Done");
    }

    [Fact]
    public void ShouldAllowAnHonestBlockedAnswerWithoutCompletedWork()
    {
        var result = _protocol.Parse("""
            {"status":"blocked","finalAnswer":"I cannot verify this with the available data."}
            """);

        result.Status.ShouldBe(RunCompletionStatus.Blocked);
        result.FinalAnswer.ShouldBe("I cannot verify this with the available data.");
    }

    [Theory]
    [InlineData("{\"status\":\"complete\"}")]
    [InlineData("{\"status\":\"continue\",\"nextAction\":\"Try again\"}")]
    [InlineData("{\"status\":\"blocked\",\"finalAnswer\":\"\"}")]
    public void ShouldRejectAnIncompleteDecision(string arguments) =>
        Should.Throw<ArgumentException>(() => _protocol.Parse(arguments));

    [Theory]
    [InlineData("{\"status\":\"Complete\",\"finalAnswer\":\"Kolya\"}")]
    [InlineData("\"{\\\"status\\\":\\\"complete\\\",\\\"finalAnswer\\\":\\\"Kolya\\\"}\"")]
    [InlineData("{\"status\":\"complete\",\"finalAnswer\":\"Kolya\",\"completed\":[\"Read memory\"]}")]
    public void ShouldAcceptLegacyArgumentsAndEncodings(string arguments)
    {
        var result = _protocol.Parse(arguments);

        result.Status.ShouldBe(RunCompletionStatus.Complete);
        result.FinalAnswer.ShouldBe("Kolya");
    }
}
