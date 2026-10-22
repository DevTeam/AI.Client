namespace AI.Application.Users;

using Domain.Users;

/// <summary>The user the current operation runs for.</summary>
public interface IUserContext
{
    /// <summary>
    /// The user of the innermost active <see cref="IUserScope"/>. Throws when no scope is active: an
    /// operation that lost its user fails instead of falling back to the local user's data.
    /// </summary>
    UserId Current { get; }
}
