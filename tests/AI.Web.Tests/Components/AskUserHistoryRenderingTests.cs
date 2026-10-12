namespace AI.Web.Tests.Components;

using AI.Contracts.Chats;
using AI.Contracts.Chat;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class AskUserHistoryRenderingTests
{
    [Fact]
    public async Task CompletedQuestionShouldShowAllQuestionsAndAnswers()
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(
            composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var now = DateTimeOffset.UtcNow;
        var call = new ChatMessageView(Guid.NewGuid(), null, "Assistant", "", now,
            ToolCalls: [new ChatToolCall("ask-1", "mcp_app__ask_user", """
                {"questions":[{"id":"install","label":"Install","text":"How should the server be installed?"},
                              {"id":"location","label":"Location","text":"Where should its files go?"}]}
                """)]);
        var result = new ChatMessageView(Guid.NewGuid(), call.Id, "Tool", """
            {"isError":false,"structuredContent":{"outcome":"answered","answers":[
                {"id":"install","selected":["Portable binary"]},
                {"id":"location","selected":[],"other":"D:\\MCP"}]}}
            """, now.AddSeconds(1), ToolCallId: "ask-1");

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ToolActivityGroup>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ToolActivityGroup.Group)] = new[] { call, result } }));
            return component.ToHtmlString();
        });

        html.ShouldContain("<dt>How should the server be installed?</dt>");
        html.ShouldContain("<dt>Where should its files go?</dt>");
        html.ShouldContain("<dd>Portable binary</dd>");
        html.ShouldContain("<dd>D:\\MCP</dd>");
        html.ShouldContain("tool-activity-batch \" open");
    }
}
