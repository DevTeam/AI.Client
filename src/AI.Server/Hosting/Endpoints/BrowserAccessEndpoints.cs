namespace AI.Server.Hosting.Endpoints;

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>One explicit local confirmation grants a browser on the published origin access.</summary>
public sealed class BrowserAccessEndpoints(ServerOptions options) : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        if (!options.PublicWeb) return;

        routes.MapGet("/connect", (HttpContext context) =>
        {
            var state = context.Request.Query["state"].ToString();
            if (!ValidState(state)) return Results.BadRequest();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'";
            var escaped = WebUtility.HtmlEncode(state);
            return Results.Content($$"""
                <!doctype html>
                <html lang="en"><head><meta charset="utf-8"><title>Connect AI Client</title>
                <style>body{font:16px system-ui;max-width:32rem;margin:12vh auto;padding:1rem;color:#eee;background:#171717}
                button{padding:.7rem 1.2rem;font:inherit;cursor:pointer}</style></head>
                <body><h1>Connect AI Client</h1>
                <p>Allow <strong>https://ai.dev-team.org</strong> to use projects, chats and tools on this computer?</p>
                <form method="post" action="/connect"><input type="hidden" name="state" value="{{escaped}}">
                <button type="submit">Allow connection</button></form></body></html>
                """, "text/html");
        });

        routes.MapPost("/connect", async (HttpContext context, IBrowserAccessService access) =>
        {
            var localOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
            if (!string.Equals(context.Request.Headers.Origin, localOrigin, StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var state = form["state"].ToString();
            if (!ValidState(state)) return Results.BadRequest();
            var token = access.Grant();
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy =
                $"default-src 'none'; script-src 'nonce-{nonce}'; frame-ancestors 'none'; base-uri 'none'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            // ASP.NET logs RedirectResult's full Location header. Keep the bearer grant in the
            // response body so it cannot appear in normal request/redirect diagnostics.
            var destination = JsonSerializer.Serialize(
                $"https://ai.dev-team.org/#host_token={token}&host_state={state}");
            return Results.Content($$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Connecting</title></head>
                <body><p>Returning to AI Client…</p>
                <script nonce="{{nonce}}">location.replace({{destination}});</script></body></html>
                """, "text/html");
        });

        routes.MapGet("/api/bridge/session", (IHostDescriptor host) => Results.Ok(new
        {
            host.ProductName,
            host.Version,
            ApiVersion = 1
        }));

        routes.MapPost("/api/bridge/revoke", (HttpContext context, IBrowserAccessService access) =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var token = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                ? authorization[7..]
                : null;
            return access.Revoke(token) ? Results.NoContent() : Results.NotFound();
        });
    }

    private static bool ValidState(string value) =>
        value.Length == 32 && value.All(Uri.IsHexDigit);
}
