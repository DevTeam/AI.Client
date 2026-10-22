namespace AI.Infrastructure.Storage;

using AI.Application.Users;

/// <summary><c>&lt;data root&gt;/users/&lt;user id&gt;</c> for the user of the current scope.</summary>
public sealed class UserStorageLocation(IInstanceStorageLocation instance, IUserContext user) : IUserStorageLocation
{
    public string RootDirectory => Path.Combine(instance.RootDirectory, "users", user.Current.Value.ToString("N"));
}
