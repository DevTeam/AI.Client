namespace AI.Web.Tests.Skills;

using AI.Contracts.Skills;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

public sealed class SkillIconRenderingTests
{
    [Fact]
    public async Task ShouldRenderEveryNamedSkillIconInsteadOfTheUnknownIconFallback()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            async Task<string> RenderAsync(string name)
            {
                var component = await renderer.RenderComponentAsync<AppIcon>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(AppIcon.Name)] = name }));
                return component.ToHtmlString();
            }

            var fallback = await RenderAsync("unknown-skill-icon");
            foreach (var name in SkillIcons.Names)
                (await RenderAsync(name)).ShouldNotBe(fallback, name);
        });
    }
}
