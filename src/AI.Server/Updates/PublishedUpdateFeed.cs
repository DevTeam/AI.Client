namespace AI.Updates;

using AI.Contracts.Updates;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>Reads the release catalog published with the public Web application.</summary>
public sealed class PublishedUpdateFeed(HttpClient http) : IUpdateFeed
{
    private const string CatalogUrl = "https://ai.dev-team.org/updates/releases.json";

    public async Task<(UpdateRelease? Release, bool StableAvailable)> FindAsync(string product, string runtime,
        string current, UpdateChannel channel, CancellationToken token)
    {
        var extension = runtime.StartsWith("win-", StringComparison.Ordinal) ? "exe"
            : runtime.StartsWith("osx-", StringComparison.Ordinal) ? "pkg" : "deb";
        var name = $"AI.{product}-{runtime}.{extension}";
        using var response = await http.GetAsync(CatalogUrl, token);
        response.EnsureSuccessStatusCode();
        var catalog = await response.Content.ReadFromJsonAsync<Catalog>(token)
            ?? throw new InvalidDataException("The update catalog is empty.");
        if (catalog.SchemaVersion != 1 || catalog.Releases is null)
            throw new InvalidDataException("The update catalog format is not supported.");
        var releases = catalog.Releases.Where(release => !release.Draft).ToArray();
        var stable = releases.Any(release => !release.Prerelease && release.Assets.Any(asset => asset.Name == name));
        var installed = UpdateVersion.Parse(current) ?? throw new InvalidOperationException("The installed version is not valid.");
        var candidates = releases.Where(release => channel == UpdateChannel.Preview || !release.Prerelease)
            .Select(release => (Release: release, Version: UpdateVersion.Parse(release.Tag)))
            .Where(item => item.Version is not null && item.Version.CompareTo(installed) > 0)
            .OrderByDescending(item => item.Version!, Comparer<UpdateVersion>.Create((left, right) => left.CompareTo(right)));
        foreach (var candidate in candidates)
        {
            var asset = candidate.Release.Assets.FirstOrDefault(item => item.Name == name);
            if (asset is null) continue;
            var digest = Digest(asset.Digest);
            var companion = candidate.Release.Assets.FirstOrDefault(item => item.Name == $"AI.Mcp.CSharp-{runtime}.deb");
            return (new UpdateRelease(candidate.Release.Tag.TrimStart('v', 'V'), candidate.Release.Prerelease,
                TrustedUrl(candidate.Release.Url), TrustedUrl(asset.Url), name, digest, asset.Size,
                companion is null ? null : TrustedUrl(companion.Url), companion is null ? null : Digest(companion.Digest)), stable);
        }
        return (null, stable);
    }

    private static string Digest(string? value) => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value[7..].All(Uri.IsHexDigit) ? value[7..]
        : throw new InvalidOperationException("The release package has no SHA-256 digest. Publish it again before updating.");

    private static string TrustedUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/DevTeam/AI.Client/releases/", StringComparison.Ordinal)
            ? value : throw new InvalidOperationException("The release URL is not from the AI Client repository.");

    public sealed record Release([property: JsonPropertyName("tag_name")] string Tag,
        bool Draft, bool Prerelease, [property: JsonPropertyName("html_url")] string Url, Asset[] Assets);
    public sealed record Asset(string Name, [property: JsonPropertyName("browser_download_url")] string Url, string? Digest, long Size);
    public sealed record Catalog(int SchemaVersion, Release[] Releases);
}
