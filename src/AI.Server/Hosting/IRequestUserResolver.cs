namespace AI.Server.Hosting;

using AI.Domain.Users;
using Microsoft.AspNetCore.Http;

/// <summary>Says which user a request comes from.</summary>
public interface IRequestUserResolver
{
    /// <summary>The request's user, or <c>null</c> when the request carries none it accepts.</summary>
    UserId? Resolve(HttpContext context);
}
