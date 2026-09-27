namespace AI.Server.Hosting.Endpoints;

using Application.Chat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class ChatCompletionEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost(
            "/api/chat/completions",
            (ChatCompletionRequest request, IChatEndpoint endpoint, CancellationToken cancellationToken) =>
                endpoint.HandleAsync(request, cancellationToken));

        routes.MapPost(
            "/api/chat/completions/stream",
            (ChatCompletionRequest request, IChatEndpoint endpoint, HttpResponse response, CancellationToken cancellationToken) =>
                endpoint.HandleStreamAsync(request, response, cancellationToken));
    }
}
