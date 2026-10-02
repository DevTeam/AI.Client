namespace AI.Updates;

using AI.Contracts.Updates;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public sealed class GitHubUpdateFeed(HttpClient http) : IUpdateFeed
{
    public async Task<(UpdateRelease? Release, bool StableAvailable)> FindAsync(string product, string runtime,
        string current, UpdateChannel channel, CancellationToken token)
    {
        var extension = runtime.StartsWith("win-", StringComparison.Ordinal) ? "exe"
            : runtime.StartsWith("osx-", StringComparison.Ordinal) ? "pkg" : "deb";
        var name = $"AI.{product}-{runtime}.{extension}";
        var releases = new List<Release>();
        // Follow pagination: prereleases must not hide an older stable release.
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/DevTeam/AI.Client/releases?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd("AI.Client-Updater/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            var batch = await response.Content.ReadFromJsonAsync<Release[]>(token) ?? [];
            releases.AddRange(batch.Where(release => !release.Draft));
            if (batch.Length < 100) break;
        }
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
}
