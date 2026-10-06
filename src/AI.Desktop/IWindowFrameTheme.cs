namespace AI.Desktop;

/// <summary>Keeps the system titlebar and frame light or dark along with the window's theme.</summary>
internal interface IWindowFrameTheme
{
    void Apply(IntPtr windowHandle, bool dark);
}
