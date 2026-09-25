namespace AI.Client.Web.Components;

/// <summary>A right-click on a link to a local path, with the pointer position the menu opens at.</summary>
public sealed record FileLinkMenuRequest(string Path, double ClientX, double ClientY);
