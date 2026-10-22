namespace AI.Server.Tests;

using AI.Application.Users;
using AI.Domain.Users;

/// <summary>
/// A fixture that drives the server graph directly stands for one request of the local user, so its
/// code runs for that user without each test entering a scope.
/// </summary>
internal sealed class LocalUserContext : IUserContext
{
    public UserId Current => UserId.Local;

    public UserId? Find() => UserId.Local;
}
