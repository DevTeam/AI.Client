namespace AI.Infrastructure.Settings;

using AI.Contracts.Settings;

/// <summary>
/// Moves tool policies written for the retired optional C# scripting server onto the built-in
/// server, which now owns <c>cs_run</c>. Settings, projects and chats are migrated as they are read,
/// so the stored documents catch up the next time they are saved.
/// </summary>
/// <remarks>
/// A policy for <c>cs_run</c> keeps its schema hash, decision, call limit and timeout and only
/// changes server. Policies for any other tool of the old server are dropped: it never had one.
/// When the old server was switched off or denied where the policies live, the user had ruled the
/// tool out there, so every moved policy becomes Deny, and a Deny for the schema the old server
/// last declared is added when no policy for the tool exists at all. A policy already stored for the
/// built-in <c>cs_run</c> with the same schema wins over a moved one.
/// </remarks>
public static class LegacyCSharpToolMigration
{
    /// <summary>
    /// The schema hash the retired server reported for <c>cs_run</c>, used for the Deny that carries
    /// a switched-off server over when no policy recorded the hash.
    /// </summary>
    public const string ScriptToolSchemaHash = "11c67a71fac5bac3265b9dc3dbba28bf7a9d9a85b9b8649df0b44ad2a20657e0";

    /// <param name="policies">The stored policies of one scope.</param>
    /// <param name="denied">Whether the old server was switched off or denied in this scope.</param>
    /// <param name="identity">Reads a policy's server, tool name and schema hash.</param>
    /// <param name="move">Returns the policy under the built-in server, as Deny when the flag says so.</param>
    /// <param name="createDeny">Creates a Deny policy for the built-in <c>cs_run</c> and the given schema hash.</param>
    public static List<T> Migrate<T>(
        IEnumerable<T> policies,
        bool denied,
        Func<T, (Guid ServerId, string Name, string SchemaHash)> identity,
        Func<T, bool, T> move,
        Func<string, T>? createDeny = null)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var all = policies.ToArray();
        var migrated = all.Where(policy => identity(policy).ServerId != LegacyCSharpMcpServer.Id).ToList();
        foreach (var policy in all.Where(policy => identity(policy) is var key
                     && key.ServerId == LegacyCSharpMcpServer.Id && key.Name == LegacyCSharpMcpServer.ScriptToolName))
        {
            if (!HasScriptPolicy(migrated, identity, identity(policy).SchemaHash))
                migrated.Add(move(policy, denied));
        }

        if (denied && createDeny is not null && !HasScriptPolicy(migrated, identity, schemaHash: null))
            migrated.Add(createDeny(ScriptToolSchemaHash));
        return migrated;
    }

    /// <summary>Whether a policy for the built-in <c>cs_run</c> exists, for the given schema or any.</summary>
    private static bool HasScriptPolicy<T>(IEnumerable<T> policies,
        Func<T, (Guid ServerId, string Name, string SchemaHash)> identity, string? schemaHash) =>
        policies.Any(policy => identity(policy) is var key
            && key.ServerId == DefaultMcpServer.Id
            && key.Name == LegacyCSharpMcpServer.ScriptToolName
            && (schemaHash is null || string.Equals(key.SchemaHash, schemaHash, StringComparison.OrdinalIgnoreCase)));
}
