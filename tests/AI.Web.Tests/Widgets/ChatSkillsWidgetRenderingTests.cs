namespace AI.Web.Tests.Widgets;

using System.Net;
using System.Text.Json;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Resources;
using AI.Contracts.Skills;
using AI.Web.Components;
using AI.Web.Navigation;
using AI.Web.Widgets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatSkillsWidgetRenderingTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task ShouldShowEachSkillWithWhoStartedIt()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync([
            new(Guid.NewGuid(), null, "User", "Go", T0,
                Resources: [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "commit", "Commit")]),
            new(Guid.NewGuid(), null, "Assistant", "", T0, ToolCalls:
            [
                new ChatToolCall("a", "mcp_app__run_skill", "{\"skillId\":\"commit\"}"),
                new ChatToolCall("s", "mcp_app__skill_search", "{\"query\":\"git\"}")
            ]),
            new(Guid.NewGuid(), null, "Tool", "", T0, ToolCallId: "a", ContentOmitted: true, ToolResultIsError: true)
        ], [new SkillDefinition("commit", "Commit changes", "", "BuiltIn", "", true,
            JsonDocument.Parse("{}").RootElement, Kind: SkillKinds.Playbook, Icon: "git-commit")]));

        html.ShouldContain("Commit changes");
        html.ShouldContain("Playbook");
        html.ShouldContain("1 from you");
        html.ShouldContain("1 by the assistant");
        html.ShouldContain("1 failed");
        html.ShouldContain("search");
        html.ShouldContain("In 1 of 1 turn");
    }

    [Fact]
    public async Task ShouldSayWhereSkillsComeFromWhileThereAreNone()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync([new(Guid.NewGuid(), null, "User", "Hi", T0)], []));

        html.ShouldContain("Skills appear here once you start one from the slash list or the assistant runs one.");
    }

    private static async Task<string> RenderAsync(IReadOnlyList<ChatMessageView> messages, IReadOnlyList<SkillDefinition> skills)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatSkills)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatSkillsWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatSkillsWidget.Widget)] = widget,
                    [nameof(ChatSkillsWidget.Messages)] = messages,
                    [nameof(ChatSkillsWidget.Skills)] = skills
                }));
            html = component.ToHtmlString();
        });
        return html;
    }
}
