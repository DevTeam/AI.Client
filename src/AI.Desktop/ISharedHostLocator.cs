namespace AI.Desktop;

internal interface ISharedHostLocator
{
    SharedHostState Find(string dataDirectory);
}

internal sealed record SharedHostState(Uri? Address, string? Error = null);
