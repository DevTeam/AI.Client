namespace AI.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class CompositionTests
{
    [Fact]
    public async Task RegistersHttpClientAndSharesCorrectionPreparationForInjectedWebComponents()
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IJSRuntime>());

        using var provider = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(services));

        var client = provider.GetRequiredService<HttpClient>();
        client.BaseAddress.ShouldBe(new Uri("http://127.0.0.1:52173/"));
        provider.GetRequiredService<AI.Web.Navigation.IAppControlHints>()
            .Attributes("settings.connection.reserved_output")["title"].ShouldNotBeNull();
        provider.GetRequiredService<AI.TextCorrection.ITextAutoCorrectionAnalyzer>()
            .Analyze("helllo", ["en"]).Single().Text.ShouldBe("hello");
        provider.GetRequiredService<AI.Web.Settings.ITextCorrectionLanguages>().Available.Count.ShouldBe(4);
        var preparation = provider.GetRequiredService<AI.TextCorrection.ITextCorrectionPreparation>();
        preparation.IsReady(["en"]).ShouldBeFalse();
        await preparation.PrepareAsync(["en"]);
        var sharedPreparation = provider.GetRequiredService<AI.TextCorrection.ITextCorrectionPreparation>();
        sharedPreparation.ShouldBeSameAs(preparation);
        sharedPreparation.IsReady(["en"]).ShouldBeTrue();
    }
}
