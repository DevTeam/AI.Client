namespace AI.Web.Navigation;

using AI.Contracts.Navigation;

/// <summary>Projects the shared control help into the attributes used by both tooltips and discovery.</summary>
public interface IAppControlHints
{
    IReadOnlyDictionary<string, object> Attributes(string? target, string? currentHint = null, bool nativeTooltip = true);
    string GetHint(string target);
    string GetSummary(string target);
}

public sealed class AppControlHints(IAppNavigationTargets targets) : IAppControlHints
{
    public string GetHint(string target) => Definition(target).Hint!;
    public string GetSummary(string target) => Definition(target).Summary ?? GetHint(target);

    private AppNavigationTarget Definition(string target) => targets.Find(target) is { Hint: not null } definition
        ? definition : throw new ArgumentException("The control has no registered help.", nameof(target));

    public IReadOnlyDictionary<string, object> Attributes(string? target, string? currentHint = null, bool nativeTooltip = true)
    {
        var attributes = new Dictionary<string, object>();
        if (target is null)
        {
            if (!string.IsNullOrWhiteSpace(currentHint)) attributes["title"] = currentHint;
            return attributes;
        }

        var hint = GetHint(target);
        attributes["data-app-target"] = target;
        attributes["data-app-hint"] = hint;
        if (nativeTooltip)
            attributes["title"] = string.IsNullOrWhiteSpace(currentHint) || currentHint == hint
                ? hint : currentHint + "\n\n" + hint;
        if (!string.IsNullOrWhiteSpace(currentHint)) attributes["data-app-ui-hint"] = currentHint;
        return attributes;
    }
}
