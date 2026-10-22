namespace AI.Infrastructure.Chat;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI.Application.Settings;
using AI.Contracts.Settings;

public sealed class ConnectionImageProbe(HttpClient http, IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets) : IConnectionImageProbe
{
    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP8zwACTGCSAQANHQEDgslx/wAAAABJRU5ErkJggg==";

    public async Task<ImageProbeResult> TestAsync(Guid connectionId, ImageProbeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter an HTTP or HTTPS Base URL.");
        if (string.IsNullOrWhiteSpace(request.Model)) throw new ArgumentException("Select a model first.");
        var key = request.ApiKey;
        if (string.IsNullOrWhiteSpace(key) && (await settings.LoadAsync(cancellationToken)).Connections
                .Any(item => item.Id == connectionId))
            key = await secrets.GetAsync("connection", connectionId, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var message = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri.ToString().TrimEnd('/') + "/chat/completions"));
        message.Content = JsonContent.Create(new
        {
            model = request.Model.Trim(),
            messages = new object[] { new { role = "user", content = new object[]
            {
                new { type = "text", text = "Describe the attached image in one short sentence." },
                new { type = "image_url", image_url = new { url = "data:image/png;base64," + Pixel } }
            } } },
            stream = false
        });
        if (!string.IsNullOrWhiteSpace(key)) message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", key.Trim());
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            if (response.IsSuccessStatusCode) return new ImageProbeResult(true, "The model accepted image input.");
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if ((int)response.StatusCode is 400 or 415 or 422 &&
                (body.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                 body.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
                 body.Contains("multimodal", StringComparison.OrdinalIgnoreCase)))
                return new ImageProbeResult(false, "The model rejected image input.");
            throw new InvalidOperationException($"The endpoint returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("The image test did not answer within 25 seconds."); }
    }
}
