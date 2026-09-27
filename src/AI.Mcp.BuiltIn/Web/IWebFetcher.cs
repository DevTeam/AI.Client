namespace AI.Mcp.BuiltIn.Web;

public interface IWebFetcher
{
    Task<WebResponse> GetAsync(Uri url, CancellationToken cancellationToken);
}
