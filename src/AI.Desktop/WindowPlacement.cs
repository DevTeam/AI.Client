namespace AI.Desktop;

/// <summary>Where the window was when it last closed.</summary>
/// <param name="X">The left edge of the normal (not maximized) window, in screen pixels.</param>
/// <param name="Y">The top edge of the normal window, in screen pixels.</param>
/// <param name="Width">The width of the normal window, in device-independent pixels.</param>
/// <param name="Height">The height of the normal window, in device-independent pixels.</param>
/// <param name="Maximized">Whether the window was maximized.</param>
internal sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);
