namespace AI.Mcp.App;

using AI.Application.Notifications;
using AI.Application.Tools;
using AI.Contracts.Navigation;
using AI.Contracts.Themes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public enum ThemeOperation { Get, Preview, Save, Activate, Discard }

/// <param name="Applied">The window carried out the request.</param>
/// <param name="Outcome">applied, refused (the window said no and changed nothing), unavailable or expired.</param>
/// <param name="Theme">What the window paints and keeps after the request, when a window answered.</param>
public sealed record AppThemeResult(bool Applied, string Outcome, string Effect, string? Error = null, AppThemeState? Theme = null);

/// <summary>
/// Builds, tries out and switches the client's colour themes from a chat. Themes are kept by the
/// person's window rather than by the Host, so every call travels to an open window on the navigation
/// channel and comes back with what the window did. A theme is a background colour and three dials;
/// the window derives every colour from them with the same engine as the studio in Settings, so a
/// model can only ask for themes the studio could have produced, and it reads the measured contrast
/// back instead of guessing whether text stays readable.
/// </summary>
[McpServerToolType]
public sealed class AppThemeTool(IAppNavigationSignal navigation) : IAppTool
{
    private const int TimeoutSeconds = 15;

    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(navigation, run, reply).Create();

    private sealed class Session(IAppNavigationSignal navigation, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(ExecuteAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Create, adjust and switch the colour themes of the person's application window. A theme is four seeds: "
                              + "background (#rrggbb, the page colour every other colour is derived from), family (dark or light; "
                              + "omitted, the background decides), contrast (0-100: how far text and surface levels spread) and "
                              + "saturation (0-100: how much of the background's hue reaches surfaces and text; 0 is neutral grey). "
                              + "Get returns the presets, the accent, the saved themes with their seeds and any preview. "
                              + "Preview paints an unsaved theme on the page right away: omitted seeds come from the current preview, "
                              + "else from themeId, else from the theme in use, so a small step names only what changes. The result "
                              + "reports its main colours and every measured contrast pair; when meetsReadableContrast is false, fix it "
                              + "before suggesting Save. Save stores the preview (plus any seeds given) and switches to it: with themeId it "
                              + "overwrites that saved theme (pass the preview's baseThemeId to improve an existing theme), without it a "
                              + "new theme is added. Activate switches to a saved theme (themeId) or a preset (system, light, dark, darkblue, "
                              + "gray, lightgray). Save and Activate take an optional accent (blue, teal, green, amber, orange, pink, purple, "
                              + "indigo); a saved theme keeps its own accent and brings it back when switched to, while the presets share one. Status colours are not part of a theme. Discard drops the preview. "
                              + "Save and Activate change the person's settings: do them only after the person agreed."
            });

        [McpServerTool(Name = "app_theme", ReadOnly = false, Destructive = false, Idempotent = false,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppThemeResult))]
        private async Task<CallToolResult> ExecuteAsync(ThemeOperation operation, string? themeId = null, string? preset = null,
            string? name = null, string? background = null, string? family = null, int? contrast = null, int? saturation = null,
            string? accent = null, CancellationToken cancellationToken = default)
        {
            AppThemeResult Failed(string error, string outcome = "unavailable") => new(false, outcome, "Nothing was changed.", error);
            // A background run has nobody at a window, and a theme changing under whoever opens one later would be a surprise.
            if (!run.Interactive)
                return reply.Reply(Failed("This run is not attached to the person's window, so its themes cannot be reached."), true);
            if (name?.Length > CustomTheme.MaxNameLength * 2 || background?.Length > 16 || themeId?.Length > 64
                || preset?.Length > 32 || family?.Length > 16 || accent?.Length > 32)
                return reply.Reply(Failed("An argument is too long.", "refused"), true);
            var request = new AppThemeRequest(operation.ToString().ToLowerInvariant(), themeId, preset, name, background, family,
                contrast, saturation, accent);
            var response = await navigation.RequestAsync(new AppNavigation(run.ProjectId, SourceChatId: run.ChatId,
                Target: "theme", Action: request.Operation, ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(TimeoutSeconds), Theme: request),
                cancellationToken);
            if (response.Outcome != "applied" || response.Theme is not { } state)
                return reply.Reply(Failed(response.Error ?? "No application window answered.", response.Outcome == "applied" ? "unavailable" : response.Outcome), true);
            return state.Error is { } error
                ? reply.Reply(new AppThemeResult(false, "refused", state.Effect, error, state), true)
                : reply.Reply(new AppThemeResult(true, "applied", state.Effect, Theme: state));
        }
    }
}
