namespace AI.Client.Infrastructure.Settings;

using AI.Client.Application.Settings;
using Credentials;
using Storage;
using System.Text;

public sealed class ProtectedGlobalSecretStore(
    ITextFileSystem fileSystem,
    IGlobalSettingsPaths paths,
    IUserDataProtector protector) : IGlobalSecretStore
{
    public async Task<string?> GetAsync(string scope, Guid id, CancellationToken cancellationToken)
    {
        var value = await fileSystem.ReadTextAsync(paths.GetSecretPath(scope, id), cancellationToken);
        return value is null ? null : Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(value)));
    }

    public async Task SetAsync(string scope, Guid id, string? value, CancellationToken cancellationToken)
    {
        var path = paths.GetSecretPath(scope, id);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (await fileSystem.ExistsAsync(path, cancellationToken)) await fileSystem.DeleteAsync(path, cancellationToken);
            return;
        }

        var protectedValue = protector.Protect(Encoding.UTF8.GetBytes(value.Trim()));
        await fileSystem.WriteTextAsync(path, Convert.ToBase64String(protectedValue), cancellationToken);
    }

    public Task<bool> ExistsAsync(string scope, Guid id, CancellationToken cancellationToken) =>
        fileSystem.ExistsAsync(paths.GetSecretPath(scope, id), cancellationToken);
}
