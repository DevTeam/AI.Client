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
}
