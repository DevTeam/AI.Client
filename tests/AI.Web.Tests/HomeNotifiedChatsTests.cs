namespace AI.Web.Tests;

using System.Collections;
using System.Reflection;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Web.Notifications;
using AI.Web.Pages;
using AI.Web.Settings;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// The sidebar's Notifications pages by a setting of its own, exactly as Recents and Scheduled do:
/// the number is this client's, it is clamped to the range the settings row offers, and a value
/// written by an older build still reads as the default. Reading a chat does not remove it from the
/// section: it keeps its row, pushed down by the chats that come after it, until it falls past the
/// window the section remembers.
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

    [Fact]
    public void AReadChatKeepsItsRow()
    {
        var project = Project();
        var now = DateTimeOffset.UtcNow;
        var chat = Chat(project.Id, now);
        var page = Page(new Home(), [Notice(project.Id, chat.Id, now, seen: true)]);
        SelectProject(page, project, [chat]);

        NotifiedChats(page).ShouldHaveSingleItem().Id.ShouldBe(chat.Id);
    }

    [Fact]
    public void ChatsStandByTheirNewestNotificationWhetherReadOrNot()
    {
        var project = Project();
        var now = DateTimeOffset.UtcNow;
        var readOlder = Chat(project.Id, now);
        var unread = Chat(project.Id, now);
        var readNewer = Chat(project.Id, now);
        var page = Page(new Home(), [
            Notice(project.Id, readOlder.Id, now.AddMinutes(-3), seen: true),
            Notice(project.Id, unread.Id, now.AddMinutes(-1), seen: false),
            Notice(project.Id, readNewer.Id, now.AddMinutes(-2), seen: true)
        ]);
        SelectProject(page, project, [readOlder, unread, readNewer]);

        NotifiedChats(page).Select(chat => chat.Id).ShouldBe([unread.Id, readNewer.Id, readOlder.Id]);
    }

    [Fact]
    public void TheSectionForgetsTheChatsPushedPastItsWindow()
    {
        var project = Project();
        var now = DateTimeOffset.UtcNow;
        // One more chat than the section remembers, every one of them read, the oldest of them first.
        var chats = Enumerable.Range(0, NotifiedChatsMore(new Home()) + 1)
            .Select(index => Chat(project.Id, now)).ToList();
        var page = Page(new Home(), chats.Select((chat, index) =>
            Notice(project.Id, chat.Id, now.AddMinutes(index - chats.Count), seen: true)));
        SelectProject(page, project, chats);

        var kept = NotifiedChats(page);

        kept.Count.ShouldBe(NotifiedChatsMore(page));
        kept[0].Id.ShouldBe(chats[^1].Id);
        kept.ShouldNotContain(chat => chat.Id == chats[0].Id);
    }

    [Fact]
    public void TheChatOpenFromTheSectionIsNeverPushedOut()
    {
        var project = Project();
        var now = DateTimeOffset.UtcNow;
        var chats = Enumerable.Range(0, NotifiedChatsMore(new Home()) + 1)
            .Select(index => Chat(project.Id, now)).ToList();
        var page = Page(new Home(), chats.Select((chat, index) =>
            Notice(project.Id, chat.Id, now.AddMinutes(index - chats.Count), seen: true)));
        SelectProject(page, project, chats);
        FoldProjectIntoNotifications(page, chats[0].Id);

        var kept = NotifiedChats(page);

        // The open chat is added on top of the window rather than within it, so a row cannot
        // vanish from under the pointer.
        kept.Count.ShouldBe(NotifiedChatsMore(page) + 1);
        kept.ShouldContain(chat => chat.Id == chats[0].Id);
    }

    private static ProjectSummary Project() =>
        new(Guid.NewGuid(), "Project", string.Empty, DateTimeOffset.UtcNow, 1);

    private static ChatSummary Chat(Guid projectId, DateTimeOffset now) =>
        new(Guid.NewGuid(), projectId, "Chat", now, 1, now);

    private static NotificationMessage Notice(Guid projectId, Guid chatId, DateTimeOffset at, bool seen) =>
        new("Chat finished", NotificationKind.Info, Guid.NewGuid(), at, projectId, chatId, null, null, seen);

    private static Home Page(Home page, IEnumerable<NotificationMessage> history)
    {
        var notifications = new Mock<INotificationService>();
        notifications.SetupGet(service => service.History).Returns(history.ToList());
        SetField(page, "Notifications", notifications.Object);
        return page;
    }

    private static void SelectProject(Home page, ProjectSummary project, IReadOnlyList<ChatSummary> chats)
    {
        SetField(page, "_selectedProject", project);
        SetField(page, "_projects", new List<ProjectSummary> { project });
        SetField(page, "_chats", chats.ToList());
    }

    // The chat lists the project as folded into the section and counts this chat as the open one.
    private static void FoldProjectIntoNotifications(Home page, Guid openChatId)
    {
        var surface = typeof(Home).GetNestedType("ChatListSurface", BindingFlags.NonPublic)!;
        SetField(page, "_projectFoldedBy", Enum.Parse(surface, "Notifications"));
        SetField(page, "_pendingChatId", openChatId);
    }

    private static List<ChatSummary> NotifiedChats(Home page) =>
        ((IEnumerable)Invoke(page, "GetNotifiedChats")!)
        .Cast<object>()
        .Select(item => (ChatSummary)item.GetType().GetProperty("Chat")!.GetValue(item)!)
        .ToList();

    private static int NotifiedChatsShown(Home page) => (int)ReadProperty(page, "NotifiedChatsShown")!;

    private static int NotifiedChatsMore(Home page) => (int)ReadProperty(page, "NotifiedChatsMore")!;

    private static void SetCount(Home page, int count) => SetField(page, "_notifiedChatCount", count);

    private static void SetField(Home page, string field, object value)
    {
        // @inject parameters arrive as properties, the page's own state as fields.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var member = (MemberInfo?)typeof(Home).GetField(field, flags)
            ?? typeof(Home).GetProperty(field, flags | BindingFlags.Public);
        switch (member)
        {
            case FieldInfo info: info.SetValue(page, value); break;
            case PropertyInfo info: info.SetValue(page, value); break;
            default: throw new InvalidOperationException($"No member named '{field}' on Home.");
        }
    }

    private static object? ReadProperty(Home page, string property) =>
        typeof(Home).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);

    private static object? Invoke(Home page, string method) =>
        typeof(Home).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
}
