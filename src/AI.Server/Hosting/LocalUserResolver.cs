namespace AI.Server.Hosting;

using AI.Domain.Users;
using Microsoft.AspNetCore.Http;

/// <summary>
/// A Host without sign-in serves only the local user. It is consulted after the origin and bridge-token
/// checks, so a request that reaches it has already been accepted.
/// </summary>
public sealed class LocalUserResolver : IRequestUserResolver
{
    public UserId? Resolve(HttpContext context) => UserId.Local;
}
