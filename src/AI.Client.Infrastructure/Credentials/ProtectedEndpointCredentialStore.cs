using AI.Client.Application.Projects;
using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Storage;
using System.Text;

namespace AI.Client.Infrastructure.Credentials;

public sealed class ProtectedEndpointCredentialStore(
    ITextFileSystem fileSystem,
    IEndpointCredentialPaths paths,
    IUserDataProtector protector) : IEndpointCredentialStore
{
    public async Task<string?> GetAsync(EndpointProfileId profileId, CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadTextAsync(paths.GetPath(profileId), cancellationToken);
        return string.IsNullOrWhiteSpace(content)
            ? null
            : Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(content)));
    }

    public async Task SetAsync(EndpointProfileId profileId, string? apiKey, CancellationToken cancellationToken)
    {
        var path = paths.GetPath(profileId);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await fileSystem.DeleteAsync(path, cancellationToken);
            return;
        }

        var protectedData = protector.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()));
        await fileSystem.WriteTextAsync(path, Convert.ToBase64String(protectedData), cancellationToken);
    }

    public Task<bool> ExistsAsync(EndpointProfileId profileId, CancellationToken cancellationToken) =>
        fileSystem.ExistsAsync(paths.GetPath(profileId), cancellationToken);
}
