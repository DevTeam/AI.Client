namespace AI.Server.Tests.Hosting;

using AI.Application.Users;
using AI.Domain.Users;
using AI.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using Shouldly;
using Xunit;

public sealed class UserScopeMiddlewareTests
{
    private static readonly UserId Other = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public async Task RunsTheRestOfTheRequestForTheResolvedUser()
    {
        var context = new AmbientUserContext();
        var middleware = new UserScopeMiddleware(Resolver(Other), context);
        UserId? seen = null;

        await middleware.InvokeAsync(new DefaultHttpContext(), async _ =>
        {
            await Task.Yield();
            seen = context.Current;
        });

        seen.ShouldBe(Other);
        Should.Throw<InvalidOperationException>(() => context.Current);
    }

    [Fact]
    public async Task RefusesARequestWithoutAUser()
    {
        var middleware = new UserScopeMiddleware(Resolver(null), new AmbientUserContext());
        var http = new DefaultHttpContext();
        var called = false;

        await middleware.InvokeAsync(http, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.ShouldBeFalse();
        http.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task EndsTheScopeWhenTheRequestFails()
    {
        var context = new AmbientUserContext();
        var middleware = new UserScopeMiddleware(Resolver(Other), context);

        await Should.ThrowAsync<IOException>(() => middleware.InvokeAsync(new DefaultHttpContext(),
            _ => throw new IOException("Failed.")));

        Should.Throw<InvalidOperationException>(() => context.Current);
    }

    [Fact]
    public void LocalUserResolverAnswersWithTheLocalUser() =>
        new LocalUserResolver().Resolve(new DefaultHttpContext()).ShouldBe(UserId.Local);

    private static IRequestUserResolver Resolver(UserId? user)
    {
        var resolver = new Mock<IRequestUserResolver>();
        resolver.Setup(item => item.Resolve(It.IsAny<HttpContext>())).Returns(user);
        return resolver.Object;
    }
}
