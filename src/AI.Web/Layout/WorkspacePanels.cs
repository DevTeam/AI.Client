namespace AI.Web.Layout;

/// <summary>
/// The side panels as the person last left them. Widths are in CSS pixels; a missing width keeps
/// the stylesheet's. Whether the chat widget column is open is kept apart, as
/// <see cref="AI.Web.Settings.ClientSettings.ChatWidgetsOpen"/>, because the page renders it.
/// </summary>
public sealed record WorkspacePanels(double? SidebarWidth, double? ChatWidgetsWidth, bool SidebarCollapsed);
