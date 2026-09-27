namespace AI.Web;

// Helper used by every `*Api` class to build a well-formed absolute URL out of the resolved
// backend address and a relative API path. The `Uri` value from `IApiBaseUrl` keeps its
// trailing slash, so plain concatenation yields "http://localhost:52173/api/settings" rather
// than a hand-rolled path join that has to think about slashes.
//
// Centralising the concatenation here also keeps `*Api` classes free of URL-handling logic:
// they keep their existing relative-path string literals ("api/projects/{id}", etc.) and the
// only thing that changes at the call site is wrapping it in `apiBase.Api("...")`.
internal static class ApiBaseUrlExtensions
{
    public static string Api(this IApiBaseUrl baseUrl, string path) =>
        $"{baseUrl.Value}{path}";
}
