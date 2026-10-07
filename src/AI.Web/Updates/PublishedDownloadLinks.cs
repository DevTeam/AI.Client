namespace AI.Web.Updates;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

/// <summary>Resolves installer links from the catalog served with the public Web application.</summary>
public sealed class PublishedDownloadLinks(NavigationManager navigation) : IPublishedDownloadLinks
{
    public async Task<PublishedDownloadCatalog> LoadAsync(CancellationToken token = default)
    {
        using var http = new HttpClient();
        var catalog = await http.GetFromJsonAsync<PublishedDownloadCatalog>(
            navigation.ToAbsoluteUri("updates/releases.json"), token);
        if (catalog is null || catalog.SchemaVersion != 1 || catalog.Releases is null)
            throw new InvalidDataException("The published download catalog is unavailable.");
        return catalog;
    }
}

public sealed record PublishedDownloadCatalog(int SchemaVersion, PublishedDownloadRelease[] Releases)
{
    public string? AssetUrl(string product, string runtime)
    {
        var extension = runtime.StartsWith("win-", StringComparison.Ordinal) ? "exe"
            : runtime.StartsWith("osx-", StringComparison.Ordinal) ? "pkg" : "deb";
        var name = $"{product}-{runtime}.{extension}";
        foreach (var release in Releases)
        {
            var url = release.Assets.FirstOrDefault(asset => asset.Name == name)?.Url;
            if (TrustedUrl(url)) return url;
        }
        return null;
    }

    public string? DesktopReleaseUrl
    {
        get
        {
            var url = Releases.FirstOrDefault(release =>
                release.Assets.Any(asset => asset.Name.StartsWith("AI.Desktop-", StringComparison.Ordinal)))?.Url;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com"
                && uri.AbsolutePath.StartsWith("/DevTeam/AI.Client/releases/tag/", StringComparison.Ordinal)
                    ? url : null;
        }
    }

    private static bool TrustedUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com"
        && uri.AbsolutePath.StartsWith("/DevTeam/AI.Client/releases/download/", StringComparison.Ordinal);
}

public sealed record PublishedDownloadRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("html_url")] string Url,
    PublishedDownloadAsset[] Assets);

public sealed record PublishedDownloadAsset(string Name,
    [property: JsonPropertyName("browser_download_url")] string Url);
