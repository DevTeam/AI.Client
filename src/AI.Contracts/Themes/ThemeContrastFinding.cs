namespace AI.Contracts.Themes;

/// <summary>
/// One measured pair of a generated palette, so the studio can show what is actually readable
/// instead of claiming the theme is fine. A required pair is one the stylesheet promises: faint text
/// reads at 4.5:1 on the page and on a surface, status words read on their tint, and what sits on the
/// solid accent block reads on it.
/// </summary>
/// <param name="Label">What the pair is, in the person's words: "Body text on background".</param>
/// <param name="ForegroundRole">The theme role of the first colour.</param>
/// <param name="BackgroundRole">The theme role of the second colour.</param>
/// <param name="Ratio">The measured WCAG contrast ratio, rounded to two decimals.</param>
/// <param name="Target">The ratio the pair must reach; 4.5 for text, 3 for a non-text indicator.</param>
public sealed record ThemeContrastFinding(string Label, string ForegroundRole, string BackgroundRole,
    double Ratio, double Target)
{
    /// <summary>Whether the pair reaches its target; a failing pair is a warning, not a refusal.</summary>
    public bool IsMet => Ratio >= Target;
}
