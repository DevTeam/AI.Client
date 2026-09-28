namespace AI.Web;

internal sealed class ClientMode(bool publicWeb) : IClientMode
{
    public bool PublicWeb { get; } = publicWeb;
}
