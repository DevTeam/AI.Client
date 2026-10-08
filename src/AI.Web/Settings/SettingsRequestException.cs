namespace AI.Web.Settings;

using System.Net;

/// <summary>The Host refused a settings request; the message is its own explanation, ready to show.</summary>
public sealed class SettingsRequestException(string message, HttpStatusCode statusCode) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The item changed on the Host after this window read it, so a retry would be refused again.</summary>
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;
}
