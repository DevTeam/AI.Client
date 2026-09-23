namespace AI.Client.Infrastructure.Credentials;

/// <summary>
/// The macOS Keychain, through <c>/usr/bin/security</c>. Going through the system tool rather than
/// Security.framework keeps the item's access list on a binary that never changes, so an updated
/// or re-signed AI.Client is not asked again for access to its own key.
/// </summary>
public sealed class KeychainMasterKeyStore(ICommandRunner commands, IMasterKeyFormat format) : IKeyringMasterKeyStore
{
    private const string Security = "/usr/bin/security";
    private const string Service = "AI.Client";
    private const string Account = "credential-key";
    // `security find-generic-password` exits with this when there is no such item.
    private const int ItemNotFound = 44;

    public byte[]? TryGetOrCreate()
    {
        var found = commands.Run(Security, ["find-generic-password", "-s", Service, "-a", Account, "-w"]);
        if (found is null) return null;
        if (found.ExitCode == 0) return format.TryDecode(found.StandardOutput);
        if (found.ExitCode != ItemNotFound) return null;

        var key = format.Create();
        var added = commands.Run(Security,
            ["add-generic-password", "-s", Service, "-a", Account, "-l", "AI.Client credential key", "-w", format.Encode(key)]);
        if (added is not { ExitCode: 0 }) return null;

        // Read back rather than trusted: a key the Keychain did not keep would encrypt credentials
        // that nothing could decrypt after a restart.
        var stored = commands.Run(Security, ["find-generic-password", "-s", Service, "-a", Account, "-w"]);
        return stored is { ExitCode: 0 } && format.TryDecode(stored.StandardOutput) is { } kept && kept.SequenceEqual(key)
            ? key
            : null;
    }
}
