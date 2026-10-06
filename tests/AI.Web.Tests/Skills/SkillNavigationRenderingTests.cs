namespace AI.Web.Tests.Skills;

using System.Text.Json;
using AI.Contracts.Skills;
using AI.Web.Components;
using AI.Web.Notifications;
using AI.Web.Settings;
using AI.Web.Skills;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class SkillNavigationRenderingTests
{
    [Theory]
    [InlineData("Built-in", false)]
    [InlineData("User", false)]
    [InlineData("Project", true)]
    public async Task ShouldOpenLinkedDocumentAfterCatalogLoads(string source, bool projectScope)
    {
        Guid? projectId = projectScope ? Guid.NewGuid() : null;
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        SkillDefinition Skill(string id, string text) => new(id, id, text, source,
            $"---\nid: {id}\nname: {id}\n---\n{text}", true, schema, ProjectId: projectId);
        var api = new Mock<ISkillApi>();
        api.Setup(item => item.ListAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Skill("first", "FIRST_DOCUMENT"), Skill("linked", "LINKED_DOCUMENT")]);
        api.Setup(item => item.ListRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var notifications = new Mock<INotificationService>();
        await using var services = new ServiceCollection()
            .AddSingleton(api.Object).AddSingleton(notifications.Object)
            .AddSingleton<ISettingsListFilter, SettingsListFilter>()
            .AddSingleton(Mock.Of<IJSRuntime>()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<SkillEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(SkillEditor.ProjectId)] = projectId,
                    [nameof(SkillEditor.SkillToEdit)] = "linked",
                    [nameof(SkillEditor.SkillSource)] = source,
                    [nameof(SkillEditor.LinkSelection)] = 1
                }));
            var html = component.ToHtmlString();
            html.ShouldContain("LINKED_DOCUMENT</textarea>");
            html.ShouldNotContain("FIRST_DOCUMENT</textarea>");
        });
        notifications.Verify(item => item.ShowError(It.IsAny<string>()), Times.Never);
        api.Verify(item => item.SaveAsync(It.IsAny<SkillWriteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
