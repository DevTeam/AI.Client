namespace AI.Web;

using System.Net.Http.Headers;
using Microsoft.JSInterop;

/// <summary>Adds the browser grant only when the UI came from the published website.</summary>
internal sealed class BridgeAuthorizationHandler(IClientMode mode, IJSRuntime jsRuntime) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (mode.PublicWeb)
        {
            var token = await jsRuntime.InvokeAsync<string?>(
                "localStorage.getItem", cancellationToken, "ai-client.host-token.v1");
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
