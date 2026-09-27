namespace AI.Infrastructure.Credentials;

/// <summary>
/// The freedesktop Secret Service (GNOME Keyring, KWallet, KeePassXC), through <c>secret-tool</c>
/// from libsecret. Unavailable without the tool or without a running service — typical for
/// servers, containers and WSL — in which case the caller falls back.
/// </summary>
public sealed class SecretServiceMasterKeyStore(ICommandRunner commands, IMasterKeyFormat format) : IKeyringMasterKeyStore
{
    private const string SecretTool = "secret-tool";
    private static readonly string[] Attributes = ["service", "AI", "key", "credential-key"];

    public byte[]? TryGetOrCreate()
    {
        var found = commands.Run(SecretTool, ["lookup", .. Attributes]);
        if (found is null) return null;
        if (found.ExitCode == 0) return format.TryDecode(found.StandardOutput);
        // A miss exits with 1 and says nothing; anything on stderr means the service itself failed.
        if (!string.IsNullOrWhiteSpace(found.StandardError)) return null;

        var key = format.Create();
        var stored = commands.Run(SecretTool, ["store", "--label", "AI credential key", .. Attributes], format.Encode(key));
        if (stored is not { ExitCode: 0 }) return null;

        // Read back rather than trusted: some services accept a write into a locked or
        // session-only collection that is gone after logout.
        var kept = commands.Run(SecretTool, ["lookup", .. Attributes]);
        return kept is { ExitCode: 0 } && format.TryDecode(kept.StandardOutput) is { } value && value.SequenceEqual(key)
            ? key
            : null;
    }
}
