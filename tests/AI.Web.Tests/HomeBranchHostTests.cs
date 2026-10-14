namespace AI.Web.Tests;

using System.Reflection;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Web.Pages;
using Shouldly;
using Xunit;

/// <summary>
/// The open chat's branches are listed under the row it was opened from: a Recents row keeps them
/// where the click was, so the sidebar does not move, and they go back under the project's row
/// whenever that row is not on screen.
/// </summary>
public sealed class HomeBranchHostTests
{
    private static readonly Type SurfaceType = typeof(Home).GetNestedType("ChatListSurface", BindingFlags.NonPublic)!;

    [Fact]
    public void BranchesStayUnderTheRecentsRowTheChatWasOpenedFrom()
    {
        var (page, chat) = CreatePageWithOpenChat();
        SetBranchHost(page, "Recents", chat.Id);

        GetBranchHost(page).ShouldBe("Recents");
    }

    [Fact]
    public void BranchesGoBackToTheProjectWhileRecentsIsFolded()
    {
        var (page, chat) = CreatePageWithOpenChat();
        SetBranchHost(page, "Recents", chat.Id);
        SetField(page, "_recentsFolded", true);

        GetBranchHost(page).ShouldBe("Project");
    }

    [Fact]
    public void AChatOpenedAnotherWayListsItsBranchesInItsProject()
    {
        var (page, _) = CreatePageWithOpenChat();
        SetBranchHost(page, "Recents", Guid.NewGuid());

        GetBranchHost(page).ShouldBe("Project");
    }

    private static (Home Page, ChatSummary Chat) CreatePageWithOpenChat()
    {
        var page = new Home();
        var now = DateTimeOffset.UtcNow;
        var project = new ProjectSummary(Guid.NewGuid(), "Project", string.Empty, now, 1);
        var other = new ProjectSummary(Guid.NewGuid(), "Other", string.Empty, now, 1);
        var chat = new ChatSummary(Guid.NewGuid(), other.Id, "Chat", now, 1, now, Kind: "conversation");
        SetField(page, "_selectedProject", other);
        SetField(page, "_projects", new List<ProjectSummary> { project, other });
        SetField(page, "_chats", new List<ChatSummary> { chat });
        SetField(page, "_selectedChat", new ChatDetails(chat.Id, other.Id, "Chat", now, now, 1, null, []));
        return (page, chat);
    }

    private static void SetBranchHost(Home page, string surface, Guid chatId)
    {
        var tupleType = typeof(ValueTuple<,,>).MakeGenericType(SurfaceType, typeof(Guid), typeof(Guid?));
        SetField(page, "_branchHost", Activator.CreateInstance(tupleType, Enum.Parse(SurfaceType, surface), chatId, null)!);
    }

    private static string GetBranchHost(Home page) =>
        typeof(Home).GetMethod("GetBranchHostSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [])!.ToString()!;

    private static void SetField(Home page, string field, object value) =>
        typeof(Home).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
}
