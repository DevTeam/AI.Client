namespace AI.Contracts.Chats;

/// <summary>
/// The teammate a branch belongs to: a name unique in the team, its role, and the accent swatch it
/// is drawn in. The colour is the server's choice; a request leaves it out.
/// </summary>
public sealed record TeamMember(string Name, string Role, string? Color = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => $"{Name} · {Role}";

    /// <summary>
    /// The CSS value of the teammate's colour, for a component's own custom property; null for a
    /// colour that is not one of the accent swatches, so nothing else ever reaches a style.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Swatch => Color is "teal" or "amber" or "purple" or "green" or "pink" or "orange" or "indigo" or "blue"
        ? $"var(--accent-swatch-{Color})" : null;
}
