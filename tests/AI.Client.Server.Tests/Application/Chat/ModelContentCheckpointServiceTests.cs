namespace AI.Client.Application.Tests.Chat;

using AI.Client.Application.Chat;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Chat;
using Shouldly;
using Xunit;

public sealed class ModelContentCheckpointServiceTests
{
    [Fact]
    public async Task ShouldReplaceCompletedCurrentTurnWorkOnlyInModelProjectionAndResetIt()
    {
        var service = new ModelContentCheckpointService();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        var largeResult = new string('x', 10_000);
        ChatCompletionMessage[] beforeCall =
        [
            new("user", "Investigate"),
            new("assistant", "", [new ChatToolCall("read-1", "file_read", "{}")]),
            new("tool", largeResult, ToolCallId: "read-1"),
            new("assistant", "", [new ChatToolCall("compact-1", "app_context_compact", "{}")])
        ];
        using var scope = service.Begin(run, (_, _) => Task.FromResult("Read the file and found the cause."));
        service.Update(run, beforeCall);

        var result = await service.CompactAsync(run, 500, CancellationToken.None);
        var complete = beforeCall.Append(new ChatCompletionMessage("tool", "compacted", ToolCallId: "compact-1")).ToArray();
        var projected = service.Apply(run, complete);

        result.Applied.ShouldBeTrue();
        result.CoveredMessages.ShouldBe(2);
        projected.Count.ShouldBe(4);
        projected[0].Content.ShouldBe("Investigate");
        projected[1].Content.ShouldContain("found the cause");
        projected[2].ToolCalls!.Single().Id.ShouldBe("compact-1");
        complete[2].Content.ShouldBeSameAs(largeResult);

        service.Reset(run).ShouldBeTrue();
        service.Apply(run, complete).ShouldBeSameAs(complete);
    }
}
