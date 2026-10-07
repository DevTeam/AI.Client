namespace AI.Server.Tests.Updates;

#pragma warning disable CA1859 // Exercise the service through its public interface.

using AI.Contracts.Updates;
using AI.Updates;
using Shouldly;
using System.Net;
using System.Net.Http.Json;
using Xunit;

public sealed class PublishedUpdateFeedTests
{
    [Theory]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0+old", "1.0.0+new", 0)]
    [InlineData("v2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-1", 1)]
    public void OrdersReleaseVersions(string left, string right, int expected) =>
        Math.Sign(UpdateVersion.Parse(left)!.CompareTo(UpdateVersion.Parse(right))).ShouldBe(expected);

    [Theory]
    [InlineData(UpdateChannel.Stable, "2.0.0")]
    [InlineData(UpdateChannel.Preview, "2.1.0-rc.10")]
    public async Task SelectsNewestCompatibleAssetAndHonorsChannel(UpdateChannel channel, string expected)
    {
        using var handler = new FeedHandler([
            Release("2.1.0-rc.2", true), Release("2.1.0-rc.10", true),
            Release("2.0.0", false), Release("3.0.0", false, "AI.Desktop-win-arm64.exe"),
            Release("9.0.0", false) with { Draft = true }]);
        using var http = new HttpClient(handler);
        IUpdateFeed feed = new PublishedUpdateFeed(http);
        var result = await feed.FindAsync("Desktop", "win-x64", "1.0.0", channel, TestContext.Current.CancellationToken);
        result.Release.ShouldNotBeNull().Version.ShouldBe(expected);
        result.StableAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task RefusesToInstallAssetsWithoutADigest()
    {
        var release = Release("2.0.0", false);
        release.Assets[0] = release.Assets[0] with { Digest = null };
        using var handler = new FeedHandler([release]);
        using var http = new HttpClient(handler);
        IUpdateFeed feed = new PublishedUpdateFeed(http);
        await Should.ThrowAsync<InvalidOperationException>(() => feed.FindAsync("Desktop", "win-x64", "1.0.0",
            UpdateChannel.Stable, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyStableChannelReportsNoStableReleases()
    {
        using var handler = new FeedHandler([Release("2.0.0-rc.1", true)]);
        using var http = new HttpClient(handler);
        IUpdateFeed feed = new PublishedUpdateFeed(http);
        var result = await feed.FindAsync("Desktop", "win-x64", "1.0.0", UpdateChannel.Stable, TestContext.Current.CancellationToken);
        result.Release.ShouldBeNull();
        result.StableAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task ReadsPublishedCatalogWithoutCallingGitHubApi()
    {
        using var handler = new FeedHandler([Release("2.0.0", false)]);
        using var http = new HttpClient(handler);
        IUpdateFeed feed = new PublishedUpdateFeed(http);

        await feed.FindAsync("Desktop", "win-x64", "1.0.0", UpdateChannel.Stable,
            TestContext.Current.CancellationToken);

        handler.RequestUri.ShouldBe(new Uri("https://ai.dev-team.org/updates/releases.json"));
    }

    private static PublishedUpdateFeed.Release Release(string version, bool prerelease, string name = "AI.Desktop-win-x64.exe") =>
        new("v" + version, false, prerelease, $"https://github.com/DevTeam/AI.Client/releases/tag/v{version}",
            [new(name, $"https://github.com/DevTeam/AI.Client/releases/download/v{version}/{name}", "sha256:" + new string('a', 64), 10)]);

    private sealed class FeedHandler(PublishedUpdateFeed.Release[] releases) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new PublishedUpdateFeed.Catalog(1, releases)) });
        }
    }
}
