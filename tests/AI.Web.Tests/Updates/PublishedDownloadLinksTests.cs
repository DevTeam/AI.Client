namespace AI.Web.Tests.Updates;

using AI.Web.Updates;
using Shouldly;
using System.Text.Json;
using Xunit;

public sealed class PublishedDownloadLinksTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PreviewReleaseProvidesInstallerLinksWhenThereIsNoStableRelease()
    {
        const string json = """
            {"schemaVersion":1,"releases":[{"tag_name":"v0.0.1-rc","html_url":"https://github.com/DevTeam/AI.Client/releases/tag/v0.0.1-rc","assets":[{"name":"AI.Host-win-x64.exe","browser_download_url":"https://github.com/DevTeam/AI.Client/releases/download/v0.0.1-rc/AI.Host-win-x64.exe"},{"name":"AI.Desktop-win-x64.exe","browser_download_url":"https://github.com/DevTeam/AI.Client/releases/download/v0.0.1-rc/AI.Desktop-win-x64.exe"}]}]}
            """;
        var catalog = JsonSerializer.Deserialize<PublishedDownloadCatalog>(json, SerializerOptions);

        catalog.ShouldNotBeNull().AssetUrl("AI.Host", "win-x64")
            .ShouldBe("https://github.com/DevTeam/AI.Client/releases/download/v0.0.1-rc/AI.Host-win-x64.exe");
        catalog.AssetUrl("AI.Desktop", "win-x64")
            .ShouldBe("https://github.com/DevTeam/AI.Client/releases/download/v0.0.1-rc/AI.Desktop-win-x64.exe");
        catalog.DesktopReleaseUrl.ShouldBe("https://github.com/DevTeam/AI.Client/releases/tag/v0.0.1-rc");
    }

    [Fact]
    public void TheLocalAiInstallerComesFromTheSameReleaseAsTheRegularOne()
    {
        // The newest release has no Local AI installer; an older one does. Offering the older one beside
        // the newer regular installer would let one page install two versions.
        const string json = """
            {"schemaVersion":1,"releases":[
              {"tag_name":"v0.0.3-rc","html_url":"https://github.com/DevTeam/AI.Client/releases/tag/v0.0.3-rc","assets":[
                {"name":"AI.Host-win-x64.exe","browser_download_url":"https://github.com/DevTeam/AI.Client/releases/download/v0.0.3-rc/AI.Host-win-x64.exe","size":116391936}]},
              {"tag_name":"v0.0.2-rc","html_url":"https://github.com/DevTeam/AI.Client/releases/tag/v0.0.2-rc","assets":[
                {"name":"AI.Host-win-x64.exe","browser_download_url":"https://github.com/DevTeam/AI.Client/releases/download/v0.0.2-rc/AI.Host-win-x64.exe","size":116391936},
                {"name":"AI.Host-win-x64-local-ai.exe","browser_download_url":"https://github.com/DevTeam/AI.Client/releases/download/v0.0.2-rc/AI.Host-win-x64-local-ai.exe","size":1390411776}]}]}
            """;
        var catalog = JsonSerializer.Deserialize<PublishedDownloadCatalog>(json, SerializerOptions).ShouldNotBeNull();

        catalog.Asset("AI.Host", "win-x64").ShouldNotBeNull().Size.ShouldBe(116391936);
        catalog.Asset("AI.Host", "win-x64", localAi: true).ShouldBeNull();

        var same = catalog with { Releases = catalog.Releases[1..] };
        same.Asset("AI.Host", "win-x64", localAi: true).ShouldNotBeNull().Url
            .ShouldBe("https://github.com/DevTeam/AI.Client/releases/download/v0.0.2-rc/AI.Host-win-x64-local-ai.exe");
        same.Asset("AI.Host", "linux-x64", localAi: true).ShouldBeNull();
    }
}
