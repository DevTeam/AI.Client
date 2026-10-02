namespace AI.Contracts.Navigation;

public sealed record AppGuideStartRequest(Guid ProjectId, string Topic = "basic", string Mode = "show",
    Guid? ChatId = null, Guid? BranchId = null, string? Language = null, string? UiLocale = null);

public sealed record AppGuideTopic(string Id, string SkillId, string Title, string Description);

public sealed class AppGuideTopics
{
    public IReadOnlyList<AppGuideTopic> All { get; } =
    [
        new("basic", "app-guide-basic", "Getting started", "Projects, chats, branches and settings"),
        new("models", "app-guide-models", "Models and context", "How requests, tools, token budgets and model connections work"),
        new("projects", "app-guide-projects", "Projects", "Organize chats and configure project access"),
        new("chats", "app-guide-chats", "Chats and context", "Messages, context references and the queue"),
        new("branches", "app-guide-branches", "Branches", "Try another approach while keeping earlier replies"),
        new("settings", "app-guide-settings", "Settings", "Appearance, connections and tool permissions"),
        new("permissions", "app-guide-permissions", "Tool permissions", "Allow, Ask, Deny, approval scopes and inherited rules"),
        new("navigation", "app-guide-navigation", "Search, panels and notifications", "Find messages, toggle sidebars and follow notifications"),
        new("widgets", "app-guide-widgets", "Chat widgets", "Choose, arrange and read the widgets beside the chat"),
        new("skills", "app-guide-skills", "Skills", "Find and run reusable instructions"),
        new("review", "app-guide-review", "Review changes", "Review files and attach comments to a conversation")
    ];
    public AppGuideTopic? Find(string id) => All.FirstOrDefault(topic => topic.Id == id);
}
