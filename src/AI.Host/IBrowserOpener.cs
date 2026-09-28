namespace AI.Host;

/// <summary>Opens an address in the user's default browser.</summary>
internal interface IBrowserOpener
{
    void Open(string url);
}
