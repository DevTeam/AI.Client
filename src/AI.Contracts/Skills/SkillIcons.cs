namespace AI.Contracts.Skills;

/// <summary>
/// What the <c>icon</c> line of a SKILL.md may hold: one of <see cref="Names"/>, which the Web app
/// draws in AppIcon, or the data of an SVG path on the same 24×24 stroked grid for an icon of its
/// own. A name stays here for good once skills can refer to it; the order is the picker's order.
/// </summary>
public static class SkillIcons
{
    public const string Default = "skill";

    public const int MaxPathLength = 2_000;

    public static bool IsValid(string icon) => Names.Contains(icon, StringComparer.Ordinal) || IsPath(icon);

    /// <summary>
    /// Path data only: a leading moveto, then commands, numbers and separators. Nothing else can
    /// reach the page, so the value needs no sanitizing where it is drawn.
    /// </summary>
    public static bool IsPath(string icon) =>
        icon.Length is > 1 and <= MaxPathLength && icon[0] is 'M' or 'm'
        && icon.All(character => character is >= '0' and <= '9' or '.' or ',' or '-' or ' '
            || "MmLlHhVvCcSsQqTtAaZz".Contains(character));

    public static readonly IReadOnlyList<string> Names =
    [
        "skill", "sparkles", "wand", "zap", "lightbulb", "target", "rocket", "play",
        "tool", "code", "terminal", "bug", "flask", "shield", "lock", "search", "eye",
        "diff", "git-branch", "fork", "message-circle", "minimize", "list-checks", "edit",
        "file", "file-text", "folder", "folder-plus", "folder-minus", "project", "package", "database",
        "memory", "eraser", "scroll", "book", "tag", "link", "globe", "languages",
        "mail", "users", "calendar", "clock", "chart", "image", "archive", "trash",
        "download", "import", "export", "refresh", "settings",
        "cpu", "circuit-board", "server", "network", "cloud", "cloud-upload", "hard-drive", "monitor", "smartphone", "wifi",
        "workflow", "git-merge", "git-commit", "brackets", "regex", "binary", "variable", "puzzle", "blocks", "filter",
        "clipboard", "clipboard-check", "file-code", "file-search", "notebook", "graduation-cap", "bookmark", "library", "table", "calculator",
        "chart-line", "chart-pie", "trending-up", "gauge", "activity", "timer", "alarm", "calendar-check", "repeat", "history",
        "palette", "brush", "pen-tool", "crop", "camera", "video", "microphone", "headphones", "music", "map"
    ];
}
