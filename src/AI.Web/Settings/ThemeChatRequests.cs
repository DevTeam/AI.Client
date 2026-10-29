namespace AI.Web.Settings;

using AI.Contracts.Themes;

/// <inheritdoc />
/// <remarks>
/// A chat works in small steps ("warmer", "a bit softer"), so the theme it is shaping is remembered
/// between requests: each preview starts from the previous one and changes only the seeds it names.
/// Nothing is written until Save or Activate, the same promise the studio makes, and a preview never
/// outlives the page: a reload shows what is saved.
/// </remarks>
public sealed class ThemeChatRequests(IThemeService theme) : IThemeChatRequests
{
    /// <summary>The roles a preview reports, enough for the model to tell what the palette looks like.</summary>
    private static readonly string[] ReportedRoles =
    [
        ThemeToken.BgSunken, ThemeToken.Bg, ThemeToken.Surface, ThemeToken.SurfaceRaised, ThemeToken.Fill,
        ThemeToken.Border, ThemeToken.TextFaint, ThemeToken.TextMuted, ThemeToken.Text, ThemeToken.TextStrong
    ];

    private Candidate? _preview;

    private sealed record Candidate(string? BaseThemeId, string Name, CustomThemeSeeds Seeds);

    public async Task<AppThemeState> HandleAsync(AppThemeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return request.Operation switch
            {
                AppThemeRequest.Get => await StateAsync("Read the themes; nothing was changed."),
                AppThemeRequest.Preview => await PreviewAsync(request),
                AppThemeRequest.Save => await SaveAsync(request),
                AppThemeRequest.Activate => await ActivateAsync(request),
                AppThemeRequest.Discard => await DiscardAsync(),
                _ => await RefusedAsync($"Unknown operation '{request.Operation}'. Use one of: {string.Join(", ", AppThemeRequest.Operations)}.")
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return await RefusedAsync(error.Message);
        }
    }

    private async Task<AppThemeState> PreviewAsync(AppThemeRequest request)
    {
        var candidate = await ResolveAsync(request);
        await theme.PreviewCustomThemeAsync(CustomTheme.Create(candidate.BaseThemeId ?? CustomTheme.PreviewId, candidate.Name, candidate.Seeds));
        _preview = candidate;
        return await StateAsync("Showing the theme on the page without saving it.");
    }

    private async Task<AppThemeState> SaveAsync(AppThemeRequest request)
    {
        var candidate = await ResolveAsync(request);
        var accent = Accent(request.Accent);
        var saved = await theme.SaveCustomThemeAsync(request.ThemeId, candidate.Name, candidate.Seeds);
        if (accent is { } chosen) await theme.SetAccentAsync(chosen);
        _preview = null;
        return await StateAsync(request.ThemeId is null
            ? $"Saved '{saved.Name}' as a new theme and switched the page to it."
            : $"Saved the changes to '{saved.Name}' and switched the page to it.");
    }

    private async Task<AppThemeState> ActivateAsync(AppThemeRequest request)
    {
        var accent = Accent(request.Accent);
        if (request.ThemeId is not null && request.Preset is not null)
            throw new ArgumentException("Activate takes either themeId or preset, not both.");
        string effect;
        if (request.ThemeId is { } id)
        {
            var saved = await FindAsync(id);
            await theme.SetActiveCustomThemeAsync(saved.Id);
            effect = $"Switched the page to '{saved.Name}'.";
        }
        else if (request.Preset is { } preset)
        {
            if (!AppThemeState.Presets.Contains(preset, StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse<ThemePreference>(preset, true, out var preference))
                throw new ArgumentException($"Unknown preset '{preset}'. Use one of: {string.Join(", ", AppThemeState.Presets)}.");
            await theme.SetAsync(preference);
            effect = $"Switched the page to the {preference.ToString().ToLowerInvariant()} preset.";
        }
        else if (accent is not null)
        {
            effect = "Changed the accent colour.";
        }
        else
        {
            throw new ArgumentException("Activate needs themeId, preset or accent.");
        }
        if (accent is { } chosen) await theme.SetAccentAsync(chosen);
        // Whatever the person switched to replaces what was only being tried out.
        _preview = null;
        return await StateAsync(effect);
    }

    private async Task<AppThemeState> DiscardAsync()
    {
        var had = _preview is not null;
        _preview = null;
        await theme.PreviewCustomThemeAsync(await theme.GetActiveCustomThemeAsync());
        return await StateAsync(had ? "Dropped the preview; the page shows the saved theme again." : "There was no preview; the page shows the saved theme.");
    }

    /// <summary>
    /// The theme a request means: the seeds it names on top of the theme it continues. That is the
    /// current preview, unless the request names a different saved theme; with neither, the theme in use,
    /// and with no saved theme in use, the studio's starting point.
    /// </summary>
    private async Task<Candidate> ResolveAsync(AppThemeRequest request)
    {
        Candidate start;
        if (_preview is { } preview && (request.ThemeId is null || request.ThemeId == preview.BaseThemeId))
            start = preview;
        else if (request.ThemeId is { } id)
        {
            var saved = await FindAsync(id);
            start = new Candidate(saved.Id, saved.Name, saved.Seeds);
        }
        else if (await theme.GetActiveCustomThemeAsync() is { } active)
            start = new Candidate(active.Id, active.Name, active.Seeds);
        else
            start = new Candidate(null, CustomTheme.DefaultName, new CustomThemeSeeds(CustomTheme.DefaultBackground));

        var seeds = start.Seeds;
        if (request.Background is { } text)
        {
            var value = text.Trim();
            if (!SrgbColor.TryParse(value.StartsWith('#') ? value : "#" + value, out var background))
                throw new ArgumentException($"'{text}' is not a #rrggbb colour.");
            // A new background may suit the other family better, unless the request names one.
            seeds = seeds with { Background = background.ToHex(), Family = null };
        }
        if (request.Family is { } family)
        {
            seeds = seeds with
            {
                Family = family.ToLowerInvariant() switch
                {
                    CustomTheme.DarkFamily => ThemeFamily.Dark,
                    CustomTheme.LightFamily => ThemeFamily.Light,
                    _ => throw new ArgumentException($"Unknown family '{family}'. Use dark or light.")
                }
            };
        }
        if (request.Contrast is { } contrast) seeds = seeds with { Contrast = contrast };
        if (request.Saturation is { } saturation) seeds = seeds with { Saturation = saturation };
        var name = request.Name is { } named ? CustomTheme.CleanName(named) : start.Name;
        return new Candidate(start.BaseThemeId, name, ThemePalette.Generate(seeds).Seeds);
    }

    private async Task<CustomTheme> FindAsync(string id) =>
        (await theme.GetCustomThemesAsync()).FirstOrDefault(saved => saved.Id == id)
        ?? throw new ArgumentException($"No saved theme has the id '{id}'. Read the list with operation get.");

    private static AccentColor? Accent(string? accent)
    {
        if (accent is null) return null;
        return AppThemeState.Accents.Contains(accent, StringComparer.OrdinalIgnoreCase)
               && Enum.TryParse<AccentColor>(accent, true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown accent '{accent}'. Use one of: {string.Join(", ", AppThemeState.Accents)}.");
    }

    private async Task<AppThemeState> RefusedAsync(string error) =>
        await StateAsync("Nothing was changed.") with { Error = error };

    private async Task<AppThemeState> StateAsync(string effect)
    {
        var themes = await theme.GetCustomThemesAsync();
        var active = await theme.GetActiveCustomThemeAsync();
        return new AppThemeState(
            effect,
            (await theme.GetAsync()).ToString().ToLowerInvariant(),
            (await theme.GetAccentAsync()).ToString().ToLowerInvariant(),
            active?.Id,
            [.. themes.Select(saved => new AppThemeSummary(saved.Id, saved.Name, AppThemeSeeds.From(saved.Seeds), saved.Id == active?.Id, saved.Accent))],
            _preview is { } preview ? Describe(preview) : null);
    }

    private static AppThemePreview Describe(Candidate candidate)
    {
        var palette = ThemePalette.Generate(candidate.Seeds);
        return new AppThemePreview(
            candidate.BaseThemeId,
            candidate.Name,
            AppThemeSeeds.From(palette.Seeds),
            ReportedRoles.Where(palette.Tokens.ContainsKey).ToDictionary(role => role, role => palette.Tokens[role]),
            palette.MeetsReadableContrast,
            palette.Findings);
    }
}
