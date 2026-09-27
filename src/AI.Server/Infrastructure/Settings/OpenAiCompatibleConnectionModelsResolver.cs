namespace AI.Infrastructure.Settings;

using AI.Application.Settings;
using AI.Contracts.Settings;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Fetches the model catalog from an OpenAI-compatible endpoint over <c>GET {baseUrl}/models</c>
/// and flattens it to <see cref="ResolvedModelInfo"/>. Tolerates the schema differences that show up
/// in the wild: missing <c>data</c>, per-model <c>id</c> missing or empty, and additional fields the
/// caller does not need.
/// </summary>
public sealed partial class OpenAiCompatibleConnectionModelsResolver(HttpClient httpClient) : IConnectionModelsResolver
{
    // The status line alone ("401 (Unauthorized)") does not let the user know which provider rejected
    // the key or why; the body is small (typically a JSON error envelope) and survives the trip.
    private const int ErrorBodyPreviewCharacters = 1000;

    public async Task<IReadOnlyList<ResolvedModelInfo>> ResolveAsync(
        string baseUrl,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is empty.");
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Base URL must be an absolute HTTP or HTTPS URL, for example https://api.openai.com/v1.");
        }

        // /v1 is implied by the editor's placeholder, but the model endpoint lives at /models relative
        // to it. Trim trailing slashes so ".../v1/" and ".../v1" both resolve to ".../v1/models".
        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/models");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        using var response = await SendAsync(request, endpoint, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(FormatError(response, body));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        using var document = ParseJson(body, endpoint);
        var root = document.RootElement;
        // Some providers return the array directly, others under { "data": [...] }. Accept both — the
        // shape is documented but not enforced, and at least one widely deployed proxy omits "data".
        var data = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var nested) ? nested : default;
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new List<ResolvedModelInfo>();
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("id", out var idElement)) continue;
            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            // Providers list the same model twice (once per region/alias); keep the first hit and drop
            // the duplicates so the dropdown stays one-line-per-model.
            if (!seen.Add(id)) continue;
            string? display = null;
            // Display name comes from "display_name" (OpenAI newer endpoints) or "title" (third-party
            // proxies that copy the OpenAI REST spec).
            if (entry.TryGetProperty("display_name", out var displayElement)
                && displayElement.ValueKind == JsonValueKind.String)
            {
                display = displayElement.GetString();
            }
            else if (entry.TryGetProperty("title", out var titleElement)
                && titleElement.ValueKind == JsonValueKind.String)
            {
                display = titleElement.GetString();
            }
            string? ownedBy = null;
            if (entry.TryGetProperty("owned_by", out var ownedByElement)
                && ownedByElement.ValueKind == JsonValueKind.String)
            {
                ownedBy = ownedByElement.GetString();
            }
            models.Add(new ResolvedModelInfo(id.Trim(), display?.Trim(), ownedBy?.Trim()));
        }
        // Sort alphabetically so the list does not dance between requests because the upstream server
        // returned the catalog in a different order each time.
        models.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
        return models;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // DNS failure, refused connection, TLS error. The inner message names the cause and the
            // prefix names what was asked, so a typo in the host reads differently from a server down.
            throw new InvalidOperationException($"Could not reach {endpoint}. {error.Message}", error);
        }
    }

    private static JsonDocument ParseJson(string body, Uri endpoint)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            // Typically a web page: the URL points at a site rather than at its API (a missing /v1),
            // or a proxy answered with its own login page.
            throw new InvalidOperationException(
                $"{endpoint} did not return JSON. Check that the Base URL points at the API, for example ends with /v1.", error);
        }
    }

    private static string FormatError(HttpResponseMessage response, string body)
    {
        var prefix = $"The endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        var trimmed = body.Trim();
        if (trimmed.Length == 0) return prefix;
        // An HTML page, usually from a proxy in between ("the requested URL could not be
        // retrieved"), is kilobytes of markup and styles; its title is the part a person can read.
        if (IsHtml(response, trimmed))
        {
            var title = HtmlTitle().Match(trimmed);
            return title.Success
                ? $"{prefix} {WebUtility.HtmlDecode(title.Groups[1].Value).ReplaceLineEndings(" ").Trim()}"
                : prefix;
        }

        var oneLine = trimmed.ReplaceLineEndings(" ");
        var preview = oneLine.Length > ErrorBodyPreviewCharacters
            ? oneLine[..ErrorBodyPreviewCharacters] + "…"
            : oneLine;
        return $"{prefix} {preview}";
    }

    private static bool IsHtml(HttpResponseMessage response, string body) =>
        response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml"
        || body.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
        || body.StartsWith("<html", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlTitle();
}
