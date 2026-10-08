namespace AI.Web.Tests;

using System.Reflection;
using AI.Web.Pages;
using AI.Web.Settings;
using Shouldly;
using Xunit;

/// <summary>
/// The sidebar's Notifications pages by a setting of its own, exactly as Recents and Scheduled do:
/// the number is this client's, it is clamped to the range the settings row offers, and a value
/// written by an older build still reads as the default.
/// </summary>
public sealed class HomeNotifiedChatsTests
{
    [Fact]
    public void TheSectionShowsTheDefaultCountUntilTheSettingSaysOtherwise()
    {
        var page = new Home();

        NotifiedChatsShown(page).ShouldBe(ClientSettings.DefaultNotifiedChatCount);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(10, 10)]
    [InlineData(0, ClientSettings.MinNotifiedChatCount)]
    [InlineData(50, ClientSettings.MaxNotifiedChatCount)]
    public void TheChosenCountIsClampedToTheRangeTheSettingOffers(int chosen, int expected)
    {
        var page = new Home();
        SetCount(page, chosen);

        NotifiedChatsShown(page).ShouldBe(expected);
    }

    [Fact]
    public void AnEntryWrittenBeforeTheSettingExistedReadsAsTheDefault()
    {
        new ClientSettings().NotifiedChatCount.ShouldBe(ClientSettings.DefaultNotifiedChatCount);
        ClientSettings.DefaultNotifiedChatCount.ShouldBeInRange(
            ClientSettings.MinNotifiedChatCount, ClientSettings.MaxNotifiedChatCount);
    }

    [Fact]
    public void TheSettingIsKeptForThisClientAlone()
    {
        var settings = new ClientSettings { NotifiedChatCount = 6 };

        settings.RecentChatCount.ShouldBe(ClientSettings.DefaultRecentChatCount);
        settings.SoonChatCount.ShouldBe(ClientSettings.DefaultSoonChatCount);
        settings.NotifiedChatCount.ShouldBe(6);
    }

    private static void SetCount(Home page, int count) =>
        typeof(Home).GetField("_notifiedChatCount", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, count);

    private static int NotifiedChatsShown(Home page) =>
        (int)typeof(Home).GetProperty("NotifiedChatsShown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
}
