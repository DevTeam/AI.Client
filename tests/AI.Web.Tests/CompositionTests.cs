namespace AI.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class CompositionTests
{
    [Fact]
    public void RegistersHttpClientForInjectedWebComponents()
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IJSRuntime>());

        using var provider = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(services));

        var client = provider.GetRequiredService<HttpClient>();
        client.BaseAddress.ShouldBe(new Uri("http://127.0.0.1:52173/"));
        provider.GetRequiredService<AI.TextCorrection.ITextCorrectionAnalyzer>()
            .Analyze("ghbdtn", ["en", "ru"]).Single().Text.ShouldBe("привет");
        provider.GetRequiredService<AI.TextCorrection.ISupportedCorrectionLayouts>().Ids.ShouldBe(["en", "ru", "fr", "es"]);
        provider.GetRequiredService<AI.Web.Settings.ITextCorrectionLanguages>().Available.Count.ShouldBe(4);
        provider.GetRequiredService<AI.TextCorrection.ITextCorrectionPreparation>().ShouldNotBeNull();
    }
}
