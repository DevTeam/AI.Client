namespace AI.Application.Users;

using Domain.Users;

/// <summary>
/// Keeps the current user in the execution context, so it follows <c>await</c> and <c>Task.Run</c>
/// without being passed through every service and repository signature.
/// </summary>
public sealed class AmbientUserContext : IUserContext, IUserScope
{
    private readonly AsyncLocal<UserId?> _current = new();

    public UserId Current => _current.Value ?? throw new InvalidOperationException("No user scope is active.");

    public UserId? Find() => _current.Value;

    public IDisposable Enter(UserId user)
    {
        var scope = new Scope(this, _current.Value);
        _current.Value = user;
        return scope;
    }

    private sealed class Scope(AmbientUserContext owner, UserId? outer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._current.Value = outer;
        }
    }
}
