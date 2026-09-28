using AI.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Приоритет: ?api= в URL → ApiBaseUrl в wwwroot/appsettings.json. Если обоих нет — падаем с
// понятной ошибкой, потому что фронт без бэкенда всё равно бесполезен.
var publicWeb = string.Equals(builder.Configuration["ClientMode"], "PublicWeb", StringComparison.Ordinal);
// The development build decides by the dev server's address: the same wwwroot served on the
// PublicWebDevelopment origin behaves as the published site, so its pairing flow can be debugged
// without a separate build. appsettings.Development.json is not published.
var developmentPublicWeb = builder.HostEnvironment.IsDevelopment()
    && builder.Configuration["PublicWebDevelopment:Origin"] is { } developmentOrigin
    && string.Equals(new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority),
        developmentOrigin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
publicWeb |= developmentPublicWeb;
// A published page must never send its browser grant to a caller-supplied API address.
var urlParameter = publicWeb ? null : ReadApiUrlFromQuery(builder.HostEnvironment.BaseAddress);
var configUrl = developmentPublicWeb
    ? builder.Configuration["PublicWebDevelopment:ApiBaseUrl"]
    : builder.Configuration["ApiBaseUrl"];
var apiBase = urlParameter ?? configUrl
    ?? throw new InvalidOperationException(
        "ApiBaseUrl is not configured. Pass ?api=http://localhost:52173/ in the URL " +
        "or set ApiBaseUrl in wwwroot/appsettings.json.");

// Resolved once, then handed to the DI composition as a string. Pure.DI's source generator
// builds a constructor for `Arg<string>("apiBaseUrl")`, so this is the only call site that
// needs to know the value before the container starts resolving anything else.
var composition = new Composition(apiBase, publicWeb);
builder.ConfigureContainer(composition);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();

static string? ReadApiUrlFromQuery(string baseAddress)
{
    var queryStart = baseAddress.IndexOf('?');
    if (queryStart < 0)
    {
        return null;
    }

    var query = baseAddress[(queryStart + 1)..];
    foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var eq = pair.IndexOf('=');
        if (eq <= 0)
        {
            continue;
        }

        var key = Uri.UnescapeDataString(pair[..eq]);
        var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
        if (string.Equals(key, "api", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
    }

    return null;
}
