namespace AI.Web.Navigation;

using AI.Contracts.Navigation;

/// <summary>
/// A move the assistant asked for, while the window shows it: counting down to it when the window
/// follows on its own, or offering it when following would take the user away from something.
/// </summary>
/// <param name="From">Where the window was when the request came, for the note after the move.</param>
public sealed record NavigationCue(Guid Id, AppNavigation Target, bool Automatic, int SecondsLeft, string From)
{
    /// <summary>The target in words: "chat “Getting started” in Hello".</summary>
    public string Where => Target switch
    {
        { ChatTitle.Length: > 0, BranchId: not null, ProjectName.Length: > 0 } => $"a branch of “{Target.ChatTitle}” in {Target.ProjectName}",
        { ChatTitle.Length: > 0, ProjectName.Length: > 0 } => $"chat “{Target.ChatTitle}” in {Target.ProjectName}",
        { ChatTitle.Length: > 0 } => $"chat “{Target.ChatTitle}”",
        { ProjectName.Length: > 0 } => $"project {Target.ProjectName}",
        _ => "the place it created"
    };
}

/// <summary>The note left after a move the assistant made, with the way back.</summary>
public sealed record NavigationArrival(Guid Id, string From, string Where);

public interface INavigationCues
{
    /// <summary>How long the window counts down before it follows a request on its own.</summary>
    int CountdownSeconds { get; }

    /// <summary>How long the note after a move stays.</summary>
    TimeSpan ArrivalNoteDuration { get; }

    /// <summary>
    /// Decides between following and offering. The window follows only while it shows the chat
    /// whose run asked, and the user is not busy there: a draft in the composer, typing in another
    /// field or scrolling a moment ago all mean their attention is somewhere a move would break.
    /// </summary>
    NavigationCue Decide(AppNavigation target, Guid? shownChatId, bool userBusy, string from);
}

public sealed class NavigationCues : INavigationCues
{
    public int CountdownSeconds => 3;

    public TimeSpan ArrivalNoteDuration => TimeSpan.FromSeconds(8);

    public NavigationCue Decide(AppNavigation target, Guid? shownChatId, bool userBusy, string from)
    {
        var automatic = !userBusy && target.SourceChatId is { } source && shownChatId == source;
        return new NavigationCue(Guid.NewGuid(), target, automatic, automatic ? CountdownSeconds : 0, from);
    }
}
