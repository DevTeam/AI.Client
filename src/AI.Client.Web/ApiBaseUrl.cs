namespace AI.Client.Web;

internal sealed class ApiBaseUrl : IApiBaseUrl
{
    public ApiBaseUrl(string value) => Value = new Uri(value, UriKind.Absolute);
    public Uri Value { get; }
}
