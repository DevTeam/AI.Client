namespace AI.Contracts.Themes;

/// <summary>
/// Which way a theme builds its ramp away from the page background: a dark theme raises surfaces and
/// text towards white, a light one lowers them towards black. The page reads the same answer from
/// <c>data-theme-family</c>, and its rules for "a light theme" key on that attribute rather than on
/// the theme's name, so a user's own light theme gets the light rules too.
/// </summary>
public enum ThemeFamily
{
    Dark,
    Light
}
