namespace AI.Domain.Tests.Users;

using Common;
using AI.Domain.Users;
using Shouldly;
using Xunit;

public sealed class UserIdTests
{
    [Fact]
    public void ConstructorThrowsDomainExceptionWhenValueIsEmpty()
    {
        var exception = Should.Throw<DomainException>(() => new UserId(Guid.Empty));
        exception.Message.ShouldBe("User ID cannot be empty.");
    }

    [Fact]
    public void LocalIsAFixedNonEmptyId()
    {
        UserId.Local.Value.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        UserId.Local.ShouldBe(new UserId(UserId.Local.Value));
    }
}
