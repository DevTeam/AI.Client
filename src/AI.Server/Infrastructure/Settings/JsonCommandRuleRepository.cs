namespace AI.Infrastructure.Settings;

using System.Text.Json;
using AI.Application.Tools.Commands;
using AI.Contracts.FileSystem;
using AI.Contracts.Settings;
using Storage;

/// <summary>
/// Keeps command rules in <c>command-rules.json</c> next to <c>settings.json</c>. They live apart
/// from chats and projects so that a rule never changes a chat's or a project's revision, and a
/// rule left behind by a deleted chat or project simply never matches again.
/// </summary>
public sealed class JsonCommandRuleRepository(IFileSystem fileSystem, IGlobalSettingsPaths paths)
    : ICommandRuleRepository, IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AsyncGate _writes = new();

    public void Dispose() => _writes.Dispose();

    private string FilePath => Path.Combine(Path.GetDirectoryName(paths.SettingsPath)!, "command-rules.json");

    public async Task<IReadOnlyList<CommandRule>> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(FilePath, cancellationToken);
        return json is null ? [] : JsonSerializer.Deserialize<CommandRule[]>(json, Options) ?? [];
    }

    public async Task<IReadOnlyList<CommandRule>> UpdateAsync(
        Func<IReadOnlyList<CommandRule>, IReadOnlyList<CommandRule>> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var lease = await _writes.EnterAsync(cancellationToken);
        var rules = update(await LoadAsync(cancellationToken));
        await fileSystem.WriteTextAsync(FilePath + ".tmp", JsonSerializer.Serialize(rules, Options), cancellationToken);
        await fileSystem.MoveAsync(FilePath + ".tmp", FilePath, true, cancellationToken);
        return rules;
    }
}
