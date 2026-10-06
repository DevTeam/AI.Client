namespace AI.Web.Tests.Widgets;

using System.Net;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Resources;
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

public sealed class ChatReferencesWidgetRenderingTests
{
    [Fact]
    public async Task ShouldListDistinctReferencesWithKindCountsAndTheEarliestMessage()
    {
        var first = Message("User", File("src/app.cs", "app.cs"));
        var second = Message("Assistant", File("src/app.cs", "app.cs"));
        var third = Message("User", Directory("src"));

        var (html, selected) = await RenderAsync([first, second, third], isGenerating: false);

        WebUtility.HtmlDecode(html).ShouldContain("2");
        WebUtility.HtmlDecode(html).ShouldContain("files");
        WebUtility.HtmlDecode(html).ShouldContain("directories");
        // The same file named twice is one row with two mentions, not two rows.
        WebUtility.HtmlDecode(html).ShouldContain("2 mentions");
        WebUtility.HtmlDecode(html).ShouldContain("In 2 of 2 turns references were named");
        selected.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldSayNothingWasNamedInAnEmptyBranch()
    {
        var (html, _) = await RenderAsync([], isGenerating: false);

        WebUtility.HtmlDecode(html)
            .ShouldContain("Files, directories, images, uploads, skills, diffs and chats the conversation refers to appear here.");
    }

    [Fact]
    public async Task ShouldFoldRowsBeyondTheVisibleLimit()
    {
        var resources = Enumerable.Range(0, 8)
            .Select(index => File($"file-{index}.cs", $"file-{index}.cs"))
            .ToArray();

        var (html, _) = await RenderAsync([Message("User", resources)], isGenerating: false);

        WebUtility.HtmlDecode(html).ShouldContain("Show 2 more");
    }

    private static async Task<(string Html, Guid? Selected)> RenderAsync(
        IReadOnlyList<ChatMessageView> messages, bool isGenerating)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatReferences)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        Guid? selected = null;
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatReferencesWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatReferencesWidget.Widget)] = widget,
                    [nameof(ChatReferencesWidget.Messages)] = messages,
                    [nameof(ChatReferencesWidget.IsGenerating)] = isGenerating,
                    [nameof(ChatReferencesWidget.OnSelectMessage)] =
                        EventCallback.Factory.Create<Guid>(renderer, id => selected = id)
                }));
            html = component.ToHtmlString();
        });
        return (html, selected);
    }

    private static ChatMessageView Message(string role, params ChatResource[] resources) =>
        new(Guid.NewGuid(), null, role, "text", DateTimeOffset.UnixEpoch, Resources: resources);

    private static ChatResource File(string path, string name) =>
        new(Guid.NewGuid(), ChatResourceKind.File, path, name);

    private static ChatResource Directory(string path) =>
        new(Guid.NewGuid(), ChatResourceKind.Directory, path);
}
