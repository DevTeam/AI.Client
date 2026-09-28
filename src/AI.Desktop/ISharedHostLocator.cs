namespace AI.Desktop;

internal interface ISharedHostLocator
{
    SharedHostState Find();
}

internal sealed record SharedHostState(Uri? Address, bool Installed, string? Error = null);
