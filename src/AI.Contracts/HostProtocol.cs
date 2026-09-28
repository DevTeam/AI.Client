namespace AI.Contracts;

/// <summary>Version of the HTTP contract shared by the public Web, Desktop and Host.</summary>
public static class HostProtocol
{
    public const string ProductName = "AI Client";
    public const int ApiVersion = 1;

    /// <summary>Where the installed Host listens, so that every client finds it without asking.</summary>
    public const string PublicHostAddress = "http://127.0.0.1:52173/";

    /// <summary>The published Web app; the only origin an installed Host accepts by default.</summary>
    public const string PublicWebOrigin = "https://ai.dev-team.org";
}
