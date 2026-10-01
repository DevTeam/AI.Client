namespace AI.Web.Widgets;

/// <summary>How one widget sits in the chat's widget column, as the person left it.</summary>
public sealed record ChatWidgetPreference(string Id, bool Hidden = false, bool Collapsed = false);

/// <summary>Which turns a widget's figures cover.</summary>
public enum ChatWidgetScope
{
    Chat,
    LastTurn,
}

/// <summary>A kind of widget the column can hold.</summary>
/// <param name="Description">One line for the list where widgets are shown and hidden.</param>
public sealed record ChatWidgetDefinition(string Id, string Title, string Icon, string Description);

public interface IChatWidgetCatalog
{
    /// <summary>Every widget this build has, in the order a new column shows them.</summary>
    IReadOnlyList<ChatWidgetDefinition> Widgets { get; }

    ChatWidgetDefinition? Find(string id);
}

public sealed class ChatWidgetCatalog : IChatWidgetCatalog
{
    public const string ChatUsage = "chat-usage";
    public const string ChatFiles = "chat-files";
    public const string ChatTools = "chat-tools";

    public IReadOnlyList<ChatWidgetDefinition> Widgets { get; } =
    [
        new(ChatUsage, "Usage", "gauge", "Context window, tokens and cost of the whole chat or the last turn"),
        new(ChatFiles, "Files", "diff", "Files the open chat changed, with lines added and removed"),
        new(ChatTools, "Tools", "tool", "Tool calls, results and most used tools in the whole chat or the last turn")
    ];

    public ChatWidgetDefinition? Find(string id) => Widgets.FirstOrDefault(widget => widget.Id == id);
}

/// <summary>
/// Keeps the saved order of widgets in step with the widgets that exist. Saved preferences outlive
/// builds: a widget added later joins at the end, one that no longer exists is dropped, and the
/// person's order of the rest is kept.
/// </summary>
public interface IChatWidgetLayout
{
    IReadOnlyList<ChatWidgetPreference> Arrange(IReadOnlyList<ChatWidgetPreference>? saved);

    /// <summary>Puts <paramref name="id"/> right before <paramref name="beforeId"/>, or last when that is null.</summary>
    IReadOnlyList<ChatWidgetPreference> Move(IReadOnlyList<ChatWidgetPreference> widgets, string id, string? beforeId);

    /// <summary>Moves <paramref name="id"/> by <paramref name="offset"/> places among the visible widgets.</summary>
    IReadOnlyList<ChatWidgetPreference> MoveBy(IReadOnlyList<ChatWidgetPreference> widgets, string id, int offset);

    IReadOnlyList<ChatWidgetPreference> Update(IReadOnlyList<ChatWidgetPreference> widgets, string id,
        Func<ChatWidgetPreference, ChatWidgetPreference> change);
}

public sealed class ChatWidgetLayout(IChatWidgetCatalog catalog) : IChatWidgetLayout
{
    public IReadOnlyList<ChatWidgetPreference> Arrange(IReadOnlyList<ChatWidgetPreference>? saved)
    {
        var known = (saved ?? [])
            .Where(item => catalog.Find(item.Id) is not null)
            .DistinctBy(item => item.Id)
            .ToList();
        known.AddRange(catalog.Widgets.Where(widget => known.All(item => item.Id != widget.Id))
            .Select(widget => new ChatWidgetPreference(widget.Id)));
        return known;
    }

    public IReadOnlyList<ChatWidgetPreference> Move(IReadOnlyList<ChatWidgetPreference> widgets, string id, string? beforeId)
    {
        if (id == beforeId || widgets.FirstOrDefault(item => item.Id == id) is not { } moved) return widgets;
        var rest = widgets.Where(item => item.Id != id).ToList();
        var index = beforeId is null ? -1 : rest.FindIndex(item => item.Id == beforeId);
        rest.Insert(index < 0 ? rest.Count : index, moved);
        return rest;
    }

    public IReadOnlyList<ChatWidgetPreference> MoveBy(IReadOnlyList<ChatWidgetPreference> widgets, string id, int offset)
    {
        // Hidden widgets keep their places but are not stepped over one by one: the person moves
        // among what they can see.
        var visible = widgets.Where(item => !item.Hidden).ToList();
        var index = visible.FindIndex(item => item.Id == id);
        if (index < 0 || offset == 0) return widgets;
        var target = Math.Clamp(index + offset, 0, visible.Count - 1);
        if (target == index) return widgets;
        var beforeId = offset > 0
            ? target + 1 < visible.Count ? visible[target + 1].Id : null
            : visible[target].Id;
        return Move(widgets, id, beforeId);
    }

    public IReadOnlyList<ChatWidgetPreference> Update(IReadOnlyList<ChatWidgetPreference> widgets, string id,
        Func<ChatWidgetPreference, ChatWidgetPreference> change) =>
        widgets.Select(item => item.Id == id ? change(item) with { Id = id } : item).ToArray();
}

/// <summary>
/// What a widget gets from the column it sits in: which one it is, how it is shown, and the
/// actions every widget header offers. A widget draws its own content inside the shared shell.
/// </summary>
public sealed record ChatWidgetContext(
    ChatWidgetDefinition Definition,
    ChatWidgetPreference Preference,
    Func<Task> ToggleCollapsed,
    Func<Task> Hide,
    Func<int, Task> Move);
