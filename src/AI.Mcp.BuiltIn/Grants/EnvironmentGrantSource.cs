namespace AI.Mcp.BuiltIn.Grants;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads session directory grants handed over by the Host through <see cref="Variable"/>.
/// Absent or malformed configuration yields no grants, so file system tools stay closed.
/// </summary>
public sealed class EnvironmentGrantSource : IGrantSource
{
    public const string Variable = "AI_CLIENT_DIRECTORY_GRANTS";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    public IReadOnlyList<DirectoryGrantSpec> Load()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        Entry[]? entries;
        try
        {
            entries = JsonSerializer.Deserialize<Entry[]>(value, Json);
        }
        catch (JsonException error)
        {
            Console.Error.WriteLine($"Ignoring {Variable}: {error.Message}");
            return [];
        }

        if (entries is null)
        {
            return [];
        }

        var grants = new List<DirectoryGrantSpec>(entries.Length);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Root))
            {
                continue;
            }

            var capabilities = new HashSet<GrantCapability>();
            foreach (var name in entry.Capabilities ?? [])
            {
                if (Enum.TryParse<GrantCapability>(name?.Trim(), true, out var capability))
                {
                    capabilities.Add(capability);
                }
            }

            if (capabilities.Count == 0)
            {
                continue;
            }

            grants.Add(new DirectoryGrantSpec(entry.Root.Trim(), entry.Recursive, capabilities));
        }

        return grants;
    }

    private sealed record Entry(string? Root, bool Recursive, string[]? Capabilities);
}
