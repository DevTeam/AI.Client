namespace AI.Web.Tests;

using System.Reflection;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Schedules;
using AI.Web.Pages;
using Shouldly;
using Xunit;

/// <summary>
/// A scheduled chat is listed by the sidebar's Scheduled section, not by Recents: it is not the
/// latest thing that happened but work the Host is going to start on its own, and listing it in
/// both would spend two of the few places on one chat.
/// </summary>
public sealed class HomeRecentsExclusionTests
{
    [Fact]
    public void ScheduledChatsAreLeftOutOfRecentsInBothTheOtherProjectsAndTheOpenOne()
    {
        var page = new Home();
        var now = DateTimeOffset.UtcNow;
        var openProject = new ProjectSummary(Guid.NewGuid(), "Open", string.Empty, now, 1);
        var otherProject = new ProjectSummary(Guid.NewGuid(), "Other", string.Empty, now, 1);
        SetField(page, "_selectedProject", openProject);
        SetField(page, "_projects", new List<ProjectSummary> { openProject, otherProject });
        var otherNormal = Summary(otherProject.Id, now, "conversation");
        var otherScheduled = Summary(otherProject.Id, now, ChatSchedule.Kind);
        SetField(page, "_recentChats", new List<ChatSummary> { otherNormal, otherScheduled });
        SetField(page, "_chats", new List<ChatSummary>
        {
            Summary(openProject.Id, now, ChatSchedule.Kind),
            Summary(openProject.Id, now, "conversation")
        });

        var recents = (List<ChatSummary>)Invoke(page, "GetRecentChats", 10)!;

        recents.ShouldContain(otherNormal);
        recents.ShouldAllBe(chat => chat.Kind != ChatSchedule.Kind);
        recents.Count.ShouldBe(2);
    }

    private static ChatSummary Summary(Guid projectId, DateTimeOffset now, string kind) =>
        new(Guid.NewGuid(), projectId, "Chat", now, 1, now, Kind: kind);

    private static void SetField(Home page, string field, object value) =>
        typeof(Home).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static object? Invoke(Home page, string method, params object[] arguments) =>
        typeof(Home).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, arguments);
}
