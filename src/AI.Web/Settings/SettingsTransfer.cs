namespace AI.Web.Settings;

using AI.Contracts.Settings;

/// <summary>
/// One connection or MCP server read from pasted or loaded settings. Secret values never come
/// through: <paramref name="Settings"/> holds none, and what was left out is described instead.
/// </summary>
/// <param name="Settings">The item with a fresh id; the planner decides whether it lands as new.</param>
/// <param name="CredentialOmitted">
/// The source named a key (a connection's <c>apiKey</c>, an MCP server's authorization header),
/// so the item needs one typed in. MCP environment secrets are marked on the variables themselves.
/// </param>
/// <param name="Notes">What the source had that could not be carried over as it was.</param>
public sealed record ImportedSettingsItem<T>(T Settings, bool CredentialOmitted, IReadOnlyList<string> Notes);

/// <summary>An entry in the source that is not imported at all, and why.</summary>
public sealed record SkippedImportItem(string Name, string Reason);

/// <param name="SecretValuesDropped">
/// A real-looking key was in the text and has been left out. The text itself may still sit in the
/// clipboard or a file, which is worth telling the user.
/// </param>
/// <param name="Error">Why nothing could be read; the lists are empty when it is set.</param>
public sealed record SettingsTransferParseResult(
    IReadOnlyList<ImportedSettingsItem<ConnectionSettings>> Connections,
    IReadOnlyList<ImportedSettingsItem<McpServerSettings>> McpServers,
    IReadOnlyList<SkippedImportItem> Skipped,
    bool SecretValuesDropped,
    string? Error)
{
    public static SettingsTransferParseResult Failed(string error) => new([], [], [], false, error);

    public bool IsEmpty => Connections.Count == 0 && McpServers.Count == 0;
}

public enum SettingsImportMatch
{
    /// <summary>Nothing by this name yet.</summary>
    New,

    /// <summary>An item by this name differs from the imported one.</summary>
    Conflict,

    /// <summary>An item by this name already says exactly this.</summary>
    Same
}

public enum SettingsImportAction
{
    Add,
    Replace,
    KeepBoth,
    Skip
}

/// <summary>
/// What importing one item would do, worked out ahead so the preview shows the consequence of each
/// choice rather than a guess.
/// </summary>
/// <param name="Imported">What was read, with the notes on what could not be carried over.</param>
/// <param name="AddAs">The item as it lands when added under its own name.</param>
/// <param name="Existing">The current item with the same name, if any.</param>
/// <param name="ReplaceWith">
/// <paramref name="Existing"/> updated from the import. It keeps the existing id, so the keys already
/// saved for it stay attached; null when the existing item is the Host's own and cannot be replaced.
/// </param>
/// <param name="MissingIfAdded">Keys to type in after adding.</param>
/// <param name="MissingIfReplaced">Keys still to type in after replacing; saved ones are kept.</param>
/// <param name="SwitchedOffIfAdded">A process the user has not reviewed yet: added switched off.</param>
/// <param name="SwitchedOffIfReplaced">Replacing changes the command, so the server is switched off.</param>
public sealed record SettingsImportCandidate<T>(
    ImportedSettingsItem<T> Imported,
    T AddAs,
    T? Existing,
    T? ReplaceWith,
    SettingsImportMatch Match,
    IReadOnlyList<string> MissingIfAdded,
    IReadOnlyList<string> MissingIfReplaced,
    bool SwitchedOffIfAdded,
    bool SwitchedOffIfReplaced) where T : class
{
    public bool CanReplace => ReplaceWith is not null;

    public SettingsImportAction DefaultAction => Match switch
    {
        SettingsImportMatch.New => SettingsImportAction.Add,
        SettingsImportMatch.Same => SettingsImportAction.Skip,
        _ => CanReplace ? SettingsImportAction.Replace : SettingsImportAction.KeepBoth
    };
}

public sealed record SettingsImportPlan(
    IReadOnlyList<SettingsImportCandidate<ConnectionSettings>> Connections,
    IReadOnlyList<SettingsImportCandidate<McpServerSettings>> McpServers,
    IReadOnlyList<SkippedImportItem> Skipped,
    bool SecretValuesDropped);

public sealed record SettingsImportChoice<T>(SettingsImportCandidate<T> Candidate, SettingsImportAction Action) where T : class;

/// <summary>What the user chose in the import preview; items left out are skipped.</summary>
public sealed record SettingsImportSelection(
    IReadOnlyList<SettingsImportChoice<ConnectionSettings>> Connections,
    IReadOnlyList<SettingsImportChoice<McpServerSettings>> McpServers);
