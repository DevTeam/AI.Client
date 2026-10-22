namespace AI.Application.Users;

using Domain.Users;

public static class UserScopeExtensions
{
    /// <summary>
    /// Starts background work for <paramref name="user"/>. The scope is entered explicitly instead of
    /// inherited, because work started for one user can be started from another's flow — a worker
    /// resumed by whoever ended a maintenance pause, a report sent to a lead branch.
    /// </summary>
    public static Task Run(this IUserScope scope, UserId user, Func<Task> work) =>
        Task.Run(async () =>
        {
            using var entered = scope.Enter(user);
            await work();
        });

    /// <inheritdoc cref="Run(IUserScope, UserId, Func{Task})"/>
    public static Task<T> Run<T>(this IUserScope scope, UserId user, Func<Task<T>> work) =>
        Task.Run(async () =>
        {
            using var entered = scope.Enter(user);
            return await work();
        });
}
