namespace AI.Web.Navigation;

using System.Text.RegularExpressions;
using AI.Contracts.Navigation;

/// <summary>Parses transcript shortcuts. Links can reveal UI or navigate, never mutate controls.</summary>
public sealed partial class AppNavigationLinks(AppNavigationTargets targets)
{
    public AppNavigation? Parse(string href, Guid? currentProjectId)
    {
        if (href.Length > 2048) return null;
        var match = LinkPattern().Match(href);
        if (!match.Success) return null;
        var target = match.Groups["target"].Value;
        var definition = targets.Find(target);
        var domain = target is "project" or "chat" or "branch";
        var chatControl = target.StartsWith("chat.", StringComparison.Ordinal) && definition?.Section is null;
        // Showing a known control is safe even when that control can perform an action.
        // The demo target is a guide command, not a destination in the visible UI.
        if (definition is null || target == "chat.demo") return null;
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var pair in match.Groups["query"].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            if (parts.Length != 2 || parts[0] is not ("projectId" or "chatId" or "branchId")
                || !Guid.TryParse(parts[1], out var id) || id == Guid.Empty || !ids.TryAdd(parts[0], id)) return null;
        }
        var project = ids.TryGetValue("projectId", out var projectId) ? projectId : currentProjectId;
        Guid? chat = ids.TryGetValue("chatId", out var chatId) ? chatId : null;
        Guid? branch = ids.TryGetValue("branchId", out var branchId) ? branchId : null;
        if ((chatControl || definition.Section is "ProjectSettings" or "ProjectPermissions" or "ChatPermissions" or "Widgets")
            && project is null) return null;
        if (definition.Section == "ChatPermissions" && chat is null) return null;
        if (domain && !ids.ContainsKey("projectId") || target is "chat" or "branch" && chat is null
            || target == "branch" && branch is null || branch is not null && chat is null
            || target == "project" && (chat is not null || branch is not null)
            || target == "chat" && branch is not null) return null;
        return new AppNavigation(project ?? Guid.Empty, chat, branch, Target: target, Action: domain ? "click" : "show");
    }

    [GeneratedRegex("^aiclient://navigate/(?<target>[a-z][a-z0-9_.-]*)(?:\\?(?<query>[^#]*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();
}
