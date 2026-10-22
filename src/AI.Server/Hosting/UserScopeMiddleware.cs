namespace AI.Server.Hosting;

using AI.Application.Users;
using Microsoft.AspNetCore.Http;

public sealed class UserScopeMiddleware(IRequestUserResolver users, IUserScope scope) : IUserScopeMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate pipeline)
    {
        if (users.Resolve(context) is not { } user)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using (scope.Enter(user))
        {
            await pipeline(context);
        }
    }
}
