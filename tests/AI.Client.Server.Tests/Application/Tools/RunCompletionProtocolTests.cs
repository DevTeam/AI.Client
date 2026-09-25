namespace AI.Client.Application.Tests.Tools;

using AI.Client.Application.Tools;
using Shouldly;
using Xunit;

public sealed class RunCompletionProtocolTests
{
    private readonly RunCompletionProtocol _protocol = new();

    [Fact]
    public void ShouldParseACompleteDefinitionOfDone()
    {
        var result = _protocol.Parse("""
            {"status":"complete","finalAnswer":"Done","completed":["Build passed"],"evidence":["0 errors"],"remaining":[]}
            """);

        result.Status.ShouldBe(RunCompletionStatus.Complete);
        result.FinalAnswer.ShouldBe("Done");
        result.Completed.ShouldBe(["Build passed"]);
    }

    [Theory]
    [InlineData("{\"status\":\"complete\",\"completed\":[],\"evidence\":[],\"remaining\":[]}")]
    [InlineData("{\"status\":\"continue\",\"completed\":[],\"evidence\":[],\"remaining\":[]}")]
    [InlineData("{\"status\":\"complete\",\"finalAnswer\":\"Done\",\"completed\":[\"x\"],\"evidence\":[],\"remaining\":[\"y\"]}")]
    public void ShouldRejectAnIncompleteDecision(string arguments) =>
        Should.Throw<ArgumentException>(() => _protocol.Parse(arguments));

    [Theory]
    [InlineData("{\"status\":\"complete\",\"finalAnswer\":\"Kolya\",\"completed\":{\"item\":\"Read memory\"},\"evidence\":{\"item\":\"Profile\"}}")]
    [InlineData("{\"status\":\"Complete\",\"finalAnswer\":\"Kolya\",\"completed\":\"Read memory\",\"evidence\":\"Profile\"}")]
    [InlineData("{\"status\":\"complete\",\"finalAnswer\":\"Kolya\",\"completed\":[{\"item\":\"Read memory\"}],\"evidence\":[]}")]
    [InlineData("\"{\\\"status\\\":\\\"complete\\\",\\\"finalAnswer\\\":\\\"Kolya\\\",\\\"completed\\\":[\\\"Read memory\\\"]}\"")]
    public void ShouldAcceptHarmlessShapeMistakes(string arguments)
    {
        var result = _protocol.Parse(arguments);

        result.Status.ShouldBe(RunCompletionStatus.Complete);
        result.FinalAnswer.ShouldBe("Kolya");
        result.Completed.ShouldBe(["Read memory"]);
    }
}
