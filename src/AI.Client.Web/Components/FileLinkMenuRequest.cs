namespace AI.Client.Web.Components;

using AI.Client.Contracts.Resources;

/// <summary>
/// A right-click on a link to a local path, with the pointer position the menu opens at. Kind is
/// known when the Host has already resolved the path, and null for a link it has not checked.
/// </summary>
public sealed record FileLinkMenuRequest(string Path, double ClientX, double ClientY, ChatResourceKind? Kind = null);
