namespace AI.Web.Components;

using AI.Web.Settings;

/// <summary>One line of the import preview: what could happen, and what the user picked.</summary>
public sealed class SettingsImportRow<T>(SettingsImportCandidate<T> candidate) where T : class
{
    public SettingsImportCandidate<T> Candidate { get; } = candidate;

    public SettingsImportAction Action { get; set; } = candidate.DefaultAction;

    public IReadOnlyList<string> Missing => Action == SettingsImportAction.Replace
        ? Candidate.MissingIfReplaced
        : Candidate.MissingIfAdded;

    public bool SwitchedOff => Action == SettingsImportAction.Replace
        ? Candidate.SwitchedOffIfReplaced
        : Candidate.SwitchedOffIfAdded;
}
