namespace AI.Client.Server.Hosting;

using Domain.Common;
using Microsoft.AspNetCore.Http;

public sealed class ApiExceptionHandler : IApiExceptionHandler
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate pipeline)
    {
        try { await pipeline(context); }
        catch (Exception error) when (!context.Response.HasStarted && error is ArgumentException or DomainException)
        {
            await Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
        }
        catch (InvalidOperationException error) when (!context.Response.HasStarted)
        {
            await Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict).ExecuteAsync(context);
        }
    }
}
