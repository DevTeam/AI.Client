namespace AI.Mcp.BuiltIn.Web;

/// <summary>
/// The HTTP client a readiness condition polls with. Separate from <see cref="IWebFetcher"/> because
/// a probe must not follow redirects: a condition is only accepted for a loopback address, and a
/// local page that redirected would otherwise carry the request to the public host the condition was
/// refused for. The two are therefore different handlers, not different settings of one call.
/// </summary>
public interface IHttpProbe
{
    Task<WebResponse> GetAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// The platform probe: the same bounded fetch a person's <c>fetch</c> call uses, built on a handler
/// that reports a redirect as a status instead of chasing it. One instance serves the whole session,
/// so the handler behind it is shared rather than created per wait.
/// </summary>
public sealed class HttpProbe : IHttpProbe, IDisposable
{
    private readonly WebFetcher _fetcher = new(WebFetcher.CreateProbeHandler());

    public Task<WebResponse> GetAsync(Uri url, CancellationToken cancellationToken) =>
        _fetcher.GetAsync(url, cancellationToken);

    public void Dispose() => _fetcher.Dispose();
}
