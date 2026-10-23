namespace AI.Web.Tests.Widgets;

using System.Net;
using AI.Contracts.Memory;
using AI.Contracts.Navigation;
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

public sealed class ChatMemoryWidgetRenderingTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Chat = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShouldListApplicableMemoryWithItsSourceAndOpenMemorySettings()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync([
            Entry(MemoryScope.User, null, MemoryKind.Profile, "Speaks Russian"),
            Entry(MemoryScope.Project, Project, MemoryKind.Preference, "Prefers Pure.DI", pinned: true),
            Entry(MemoryScope.Project, Project, MemoryKind.Fact, "Facts the assistant kept", chatId: Chat),
            Entry(MemoryScope.User, null, MemoryKind.Fact, "Switched off", enabled: false)
        ]));

        html.ShouldContain("Speaks Russian");
        html.ShouldContain("Prefers Pure.DI");
        html.ShouldContain("Facts the assistant kept");
        html.ShouldNotContain("Switched off");
        html.ShouldContain("saved here");
        html.ShouldContain("pinned");
        html.ShouldContain("Open Prefers Pure.DI in Memory settings");
        html.ShouldContain("Seen in this project's chats: 1 about you, 2 about the project");
    }

    [Fact]
    public async Task ShouldSayWhatAppliesOnlyToThePersonWhenTheChatHasNoProject()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync(
            [Entry(MemoryScope.User, null, MemoryKind.Preference, "Answers in Russian")], hasProject: false));

        html.ShouldContain("Answers in Russian");
        html.ShouldContain("Seen in every chat of every project");
    }

    [Fact]
    public async Task ShouldSayWhenNothingAppliesYet()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync([]));

        html.ShouldContain("Memory appears here once you or the assistant save an entry that applies to this chat.");
        html.ShouldNotContain("chat-widget-row");
    }

    [Fact]
    public async Task ShouldShowSixEntriesAndTheRestBehindShowMore()
    {
        var entries = Enumerable.Range(1, 8)
            .Select(index => Entry(MemoryScope.User, null, MemoryKind.Fact, $"Entry {index}", updated: T0.AddMinutes(index)))
            .ToArray();

        var html = WebUtility.HtmlDecode(await RenderAsync(entries));

        html.ShouldContain("Entry 8");
        html.ShouldContain("Entry 3");
        html.ShouldNotContain("Entry 2");
        html.ShouldContain("Show 2 more");
    }

    private static async Task<string> RenderAsync(IReadOnlyList<MemoryEntry> entries, bool hasProject = true)
    {        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatMemory)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatMemoryWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatMemoryWidget.Widget)] = widget,
                    [nameof(ChatMemoryWidget.Entries)] = entries,
                    [nameof(ChatMemoryWidget.ProjectId)] = hasProject ? Project : null,
                    [nameof(ChatMemoryWidget.ChatId)] = Chat
                }));
            html = component.ToHtmlString();
        });
        return html;
    }

    private static MemoryEntry Entry(MemoryScope scope, Guid? projectId, MemoryKind kind, string title,
        bool pinned = false, bool enabled = true, Guid? chatId = null, DateTimeOffset? updated = null) =>
        new(Guid.NewGuid(), scope, projectId, kind, title, $"{title} body", [], pinned, enabled,
            chatId is null ? MemoryAuthor.User : MemoryAuthor.Model, chatId, T0, updated ?? T0, 1);
}
