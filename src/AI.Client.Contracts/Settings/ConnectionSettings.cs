namespace AI.Client.Contracts.Settings;

public sealed record ConnectionSettings(
    Guid Id,
    string Name,
    string BaseUrl,
    string Model,
    bool Enabled,
    bool IsDefault,
    bool HasCredential);
