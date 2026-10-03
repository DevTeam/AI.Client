namespace AI.Contracts.Instructions;

/// <summary>
/// Behavior rules the person wrote for one project. They reach the model in full on every run,
/// together with instruction files such as AGENTS.md found in the project's directory grants.
/// </summary>
public sealed record ProjectInstructions(
    Guid ProjectId,
    string Text,
    bool IncludeWorkspaceFiles,
    long Revision,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateProjectInstructionsRequest(string Text, bool IncludeWorkspaceFiles, long Revision);

/// <summary>The standing part of the system prompt, layer by layer, as the next run will send it.</summary>
public sealed record ModelContextPreview(IReadOnlyList<ModelContextLayer> Layers, long TotalTokens,
    long ContextWindowTokens = 0, long InstructionBudgetTokens = 0, long ToolBudgetTokens = 0, string? Profile = null);

/// <param name="Key">Stable layer identity: app.base, project.instructions or memory.index.</param>
/// <param name="Sources">Where the text came from, such as the project's own instructions or a file path.</param>
/// <param name="Truncated">True when an optional projection was reduced or an instruction file exceeded its read limit.</param>
public sealed record ModelContextLayer(
    string Key,
    string Title,
    IReadOnlyList<ModelContextSource> Sources,
    string Content,
    long Tokens,
    long BudgetTokens,
    bool Truncated);

/// <param name="Name">A display label, or the full path of an instruction file.</param>
/// <param name="Included">False when the source was found but left out, for example by the budget.</param>
public sealed record ModelContextSource(string Name, long Tokens, bool Included);
