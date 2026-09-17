namespace AI.Client.Web.Tests.Presentation;

using AI.Client.Contracts.Runs;
using AI.Client.Web.Markdown;
using AI.Client.Web.Runs;
using Shouldly;
using Xunit;

/// <summary>
/// The parts of a question's presentation that live outside the component: whether a chat with one
/// open says so in the sidebar, and what the question text is allowed to render as.
/// </summary>
public sealed class UserPromptPresentationTests
{
    private static ChatRunSnapshot Run(UserPrompt? prompt) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ChatRunStatus.Generating, string.Empty, [], false, null, 1,
        PendingPrompt: prompt);

    private static UserPrompt Prompt() => new(Guid.NewGuid(),
        [new UserPromptQuestion("scope", "How far?", null, [], false, true)], 900);

    [Fact]
    public void ChatWaitingForAnAnswerShouldAskForAttention()
    {
        var waiting = Run(Prompt());

        // Someone looking at another chat has to be able to see that this one stopped for them.
        RunStatusPresentation.HasVisibleAttention(waiting).ShouldBeTrue();
        RunStatusPresentation.GetStatusClass(waiting).ShouldBe("run-status-attention");
        RunStatusPresentation.GetStatusTooltip(waiting).ShouldBe("Waiting for your answer");

        RunStatusPresentation.HasVisibleAttention(Run(null)).ShouldBeFalse();
    }

    [Fact]
    public void WaitingForAnAnswerShouldNotCountAsTheModelGenerating()
    {
        var service = new RunStateService();
        var waiting = Run(Prompt());
        service.Store(waiting);

        // Reading a question is the person's time, not the endpoint's; counting it as generating
        // made the indicator claim work that nothing was doing.
        service.GetLlmGeneratingElapsed(new RunKey(waiting.ChatId, waiting.BranchId)).ShouldBeNull();
    }

    [Theory]
    [InlineData("Target `net10.0`?", "Target <code>net10.0</code>?")]
    [InlineData("Rename **now**?", "Rename <strong>now</strong>?")]
    // Block syntax needs its own lines to be block syntax, and folding the text onto one line is
    // what keeps a question the size of a question.
    [InlineData("Pick one:\n- a\n- b", "Pick one: - a - b")]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    public void QuestionTextShouldRenderAsInlineMarkupOnly(string markdown, string expected) =>
        new SafeMarkdownRenderer().RenderInline(markdown).ShouldBe(expected);
}
