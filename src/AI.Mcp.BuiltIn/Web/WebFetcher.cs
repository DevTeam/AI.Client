namespace AI.Mcp.BuiltIn.Web;

using System.Net;
using System.Net.Http.Headers;
using System.Text;

public sealed class WebFetcher : IWebFetcher, IDisposable
{
    public const int BodyLimit = 5 * 1024 * 1024;

    private readonly HttpClient _client;

    /// <summary>
    /// Production handler — a SocketsHttpHandler tuned for short, bounded fetches. The container
    /// passes it in; tests can pass a handler whose responses they control.
    /// </summary>
    public WebFetcher(HttpMessageHandler handler) : this(BuildClient(handler))
    {
    }

    // Internal so the contract stays at <see cref="HttpMessageHandler"/> for callers while the
    // container can still resolve the HttpClient it actually composes around.
    internal WebFetcher(HttpClient client)
    {
        _client = client;
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AI-BuiltIn", "1.0"));
        _client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,text/plain;q=0.8,*/*;q=0.5");
    }

    private static HttpClient BuildClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = BodyLimit
        };
    }

    internal static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10)
    };

    public async Task<WebResponse> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var encoding = Resolve(response.Content.Headers.ContentType?.CharSet);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[8192];
            using var memory = new MemoryStream();
            var truncated = false;
            while (await stream.ReadAsync(buffer, cancellationToken) is var read && read != 0)
            {
                var keep = Math.Min(read, BodyLimit - (int)memory.Length);
                memory.Write(buffer, 0, keep);
                if (keep == read)
                {
                    continue;
                }

                truncated = true;
                break;
            }

            return new WebResponse(
                (int)response.StatusCode,
                (response.RequestMessage?.RequestUri ?? url).ToString(),
                contentType,
                encoding.GetString(memory.GetBuffer(), 0, (int)memory.Length),
                truncated,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebResponse(0, url.ToString(), "", "", false, "The request timed out.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException)
        {
            return new WebResponse(0, url.ToString(), "", "", false, error.Message);
        }
    }

    public void Dispose() => _client.Dispose();

    private static Encoding Resolve(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"', '\'', ' '));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
