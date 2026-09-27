namespace AI.Application.Settings;

public interface IGlobalSecretStore
{
    Task<string?> GetAsync(string scope, Guid id, CancellationToken cancellationToken);

    Task SetAsync(string scope, Guid id, string? value, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string scope, Guid id, CancellationToken cancellationToken);
}
