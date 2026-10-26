namespace AI.Agent.Evals;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One task the harness is given, as <c>scenario.json</c> in its own directory. The directory's
/// <c>workspace</c> folder, when there is one, is copied for every attempt and granted to the project,
/// so an attempt can change it freely and the next one starts from the same files.
/// </summary>
/// <param name="Turns">
/// The user's messages, sent one after another, each once the previous turn has ended.
/// <c>{workspace}</c> is replaced with the absolute path of the attempt's copy.
/// </param>
/// <param name="Prompts">How the simulated user answers <c>ask_user</c> questions, first match wins.</param>
/// <param name="DenyTools">Tool names the simulated user refuses; every other approval is allowed.</param>
/// <param name="Repository">
/// A commit of a real repository to work on. Its files are exported without history, so the fix a
/// later commit made cannot be read from the log; a <c>workspace</c> folder is then laid over them,
/// to plant a bug or add files.
/// </param>
/// <param name="HiddenFiles">
/// Files of <see cref="Repository"/> at a later commit — usually the tests of the fix — written into
/// the workspace after the model has finished and before the checks run.
/// </param>
/// <param name="Git">Make the workspace a git repository with one commit, as a real project would be.</param>
/// <param name="ContextWindow">The window the connection declares for this scenario; a small one forces compaction.</param>
public sealed record Scenario(
    string Id,
    string Description,
    IReadOnlyList<string> Turns,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<PromptRule>? Prompts = null,
    IReadOnlyList<string>? DenyTools = null,
    int TimeoutSeconds = 900,
    IReadOnlyList<Check>? Checks = null,
    RepositorySource? Repository = null,
    IReadOnlyList<HiddenFile>? HiddenFiles = null,
    bool Git = true,
    long? ContextWindow = null,
    string? Platform = null)
{
    [JsonIgnore]
    public string Directory { get; init; } = "";

    [JsonIgnore]
    public string? Workspace => Path.Combine(Directory, "workspace") is var path && System.IO.Directory.Exists(path) ? path : null;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// The scenarios under <paramref name="root"/> that <paramref name="filter"/> names. Without a filter,
    /// scenarios tagged <c>slow</c> — an attempt can take most of an hour — are left out unless
    /// <paramref name="includeSlow"/> asks for them; a filter that names one includes it.
    /// </summary>
    public static IReadOnlyList<Scenario> Load(string root, string? filter, bool includeSlow = false)
    {
        var scenarios = new List<Scenario>();
        foreach (var file in System.IO.Directory.EnumerateFiles(root, "scenario.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            Scenario scenario;
            try
            {
                scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(file), Json)
                    ?? throw new InvalidDataException($"{file} is empty.");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"{file}: {error.Message}", error);
            }

            if (string.IsNullOrWhiteSpace(scenario.Id) || scenario.Turns is not { Count: > 0 })
                throw new InvalidDataException($"{file} needs an id and at least one turn.");
            scenario = scenario with { Directory = Path.GetDirectoryName(file)! };
            var supported = scenario.Platform switch
            {
                null => true,
                "windows" => OperatingSystem.IsWindows(),
                "linux" => OperatingSystem.IsLinux(),
                "macos" => OperatingSystem.IsMacOS(),
                _ => throw new InvalidDataException($"{file}: unsupported platform '{scenario.Platform}'.")
            };
            if (!supported) continue;
            var slow = scenario.Tags?.Contains("slow", StringComparer.OrdinalIgnoreCase) == true;
            if (filter is null ? includeSlow || !slow : Matches(scenario, filter)) scenarios.Add(scenario);
        }

        var duplicate = scenarios.GroupBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Scenario id '{duplicate.Key}' is used more than once.");
        return scenarios;
    }

    // A filter names scenarios by id substring or by tag ("tag:edit"), several separated by commas.
    private static bool Matches(Scenario scenario, string filter) => filter
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(part => part.StartsWith("tag:", StringComparison.Ordinal)
            ? scenario.Tags?.Contains(part[4..], StringComparer.OrdinalIgnoreCase) == true
            : scenario.Id.Contains(part, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Path">A local clone, absolute or relative to this repository's root (<c>../Pure.DI</c> for a sibling checkout).</param>
public sealed record RepositorySource(string Path, string Commit);

/// <param name="Commit">
/// The commit of the scenario's repository to take the file from; without one it is the scenario's
/// own <c>hidden/</c> copy of <paramref name="Path"/>.
/// </param>
public sealed record HiddenFile(string Path, string? Commit = null);

/// <summary>
/// An answer to <c>ask_user</c> questions whose text matches <paramref name="Match"/>: the first option
/// whose label matches <paramref name="Option"/>, or the free text <paramref name="Answer"/>.
/// <paramref name="Dismiss"/> leaves the choice to the model.
/// </summary>
public sealed record PromptRule(string Match, string? Option = null, string? Answer = null, bool Dismiss = false,
    IReadOnlyList<string>? Paths = null);

public enum CheckType
{
    /// <summary>A program run in the workspace exits with <see cref="Check.ExitCode"/> (and prints <see cref="Check.OutputPattern"/>).</summary>
    Command,
    FileExists,
    FileAbsent,
    /// <summary>The file's text matches <see cref="Check.Pattern"/>.</summary>
    FileContains,
    FileNotContains,
    /// <summary>The file is byte-for-byte what the fixture had.</summary>
    FileUnchanged,
    /// <summary>The final answer matches <see cref="Check.Pattern"/>.</summary>
    AnswerMatches,
    AnswerNotMatches,
    /// <summary>No file of the workspace was added, changed or deleted.</summary>
    NoWorkspaceChanges,
    /// <summary>Every change lies under one of <see cref="Check.Paths"/>.</summary>
    OnlyChanged,
    /// <summary>A text file of the workspace (under <see cref="Check.Paths"/> when given) matches <see cref="Check.Pattern"/>.</summary>
    WorkspaceContains,
    WorkspaceNotContains,
    /// <summary>The attempt made at most <see cref="Check.Value"/> tool calls.</summary>
    MaxToolCalls,
    /// <summary>The attempt made at most <see cref="Check.Value"/> failed tool calls.</summary>
    MaxToolErrors,
    /// <summary>Some tool call, as "name arguments", matches <see cref="Check.Pattern"/>.</summary>
    CallsMatch,
    /// <summary>No tool call matches <see cref="Check.Pattern"/> — "do not run the tests" kept.</summary>
    CallsNotMatch,
    /// <summary>The model asked the person (<c>ask_user</c>) at least <see cref="Check.Value"/> times.</summary>
    MinPrompts,
    MaxPrompts,
    /// <summary>The final project has exactly Value ordinary directory grants.</summary>
    ProjectGrantCount,
    /// <summary>A stored grant, as JSON, matches Pattern.</summary>
    ProjectGrantMatches,
    /// <summary>A tool call and its actual result, in order, match Pattern.</summary>
    ToolTranscriptMatches,
    MaxMatchingCalls
}

/// <param name="Pattern">A .NET regular expression, matched case-insensitively.</param>
/// <param name="Path">A path relative to the workspace, with forward slashes.</param>
/// <param name="OutputPattern">For a command, a pattern its standard output and error must also match.</param>
/// <param name="Input">For a command, what is written to its standard input before it is closed.</param>
public sealed record Check(
    CheckType Type,
    string? Name = null,
    string? Path = null,
    IReadOnlyList<string>? Paths = null,
    string? Pattern = null,
    string? Executable = null,
    IReadOnlyList<string>? Arguments = null,
    int ExitCode = 0,
    int TimeoutSeconds = 300,
    int Value = 0,
    string? OutputPattern = null,
    string? Input = null);
