namespace AI.Server.Hosting.Endpoints;

using AI.Contracts;
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
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'";
            var escaped = WebUtility.HtmlEncode(state);
            return Results.Content($$$"""
                <!doctype html>
                <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>Allow browser access · AI Client</title>
                <style>
                :root{color-scheme:light dark;font:16px/1.5 system-ui,sans-serif;background:#171717;color:#f4f4f4}
                body{box-sizing:border-box;max-width:34rem;margin:min(12vh,6rem) auto;padding:1.25rem}
                main{padding:clamp(1.4rem,5vw,2.4rem);border:1px solid #424242;border-radius:1rem;background:#242424}
                .eyebrow{color:#9dc7f3;font-size:.75rem;font-weight:700;letter-spacing:.07em;text-transform:uppercase}
                h1{margin:.5rem 0;font-size:clamp(1.7rem,5vw,2.2rem);line-height:1.15}
                p{color:#d3d3d3}strong{color:#fff}ul{padding-left:1.25rem;color:#d3d3d3}
                li+li{margin-top:.35rem}.origin{padding:.7rem .8rem;border-radius:.5rem;background:#333;overflow-wrap:anywhere}
                .actions{display:flex;flex-wrap:wrap;align-items:center;gap:1rem;margin-top:1.5rem}
                button,.cancel{min-height:2.6rem;padding:.55rem 1rem;border-radius:.5rem;font:inherit;text-decoration:none}
                button{border:0;background:#70aef0;color:#111;cursor:pointer;font-weight:650}
                .cancel{display:inline-flex;align-items:center;color:#d3d3d3}
                :is(button,.cancel):focus-visible{outline:2px solid #9dc7f3;outline-offset:3px}
                small{display:block;margin-top:1rem;color:#aaa}
                @media(prefers-color-scheme:light){:root{background:#f4f5f7;color:#20242a}main{background:#fff;border-color:#d6dce4}.eyebrow{color:#1a64b8}p,ul,.cancel{color:#414954}strong{color:#171c22}.origin{background:#eef2f7}button{background:#2a78d0;color:#fff}small{color:#626b75}}
                </style></head>
                <body><main><span class="eyebrow">AI Client Host · This computer</span>
                <h1>Allow this browser to connect?</h1>
                <p>The following website is requesting access to your local Host:</p>
                <p class="origin"><strong>https://ai.dev-team.org</strong></p>
                <ul><li>View and change your projects and chats</li><li>Use saved connections and enabled tools</li></ul>
                <form method="post" action="/connect"><input type="hidden" name="state" value="{{{escaped}}}">
                <div class="actions"><button type="submit">Allow connection</button>
                <a class="cancel" href="https://ai.dev-team.org/">Cancel</a></div></form>
                <small>You can disconnect this browser later from the Web app.</small></main></body></html>
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
            HostProtocol.ApiVersion
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
