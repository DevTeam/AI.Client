namespace AI.Application.Settings;

public sealed class SettingsConflictException(string message) : InvalidOperationException(message);
