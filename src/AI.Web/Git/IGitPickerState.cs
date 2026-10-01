namespace AI.Web.Git;

using AI.Contracts.Git;

public interface IGitPickerState
{
    bool IsLoading { get; }
    bool HasMore { get; }
    string? ErrorMessage { get; }
    string Filter { get; set; }
    IReadOnlyList<GitChoice> Items { get; }
    IReadOnlyList<string> Values { get; }
    Task OpenAsync(string repositoryPath, string kind, bool multiSelect, string? revision, IReadOnlyList<string> selected, CancellationToken token);
    Task LoadMoreAsync(CancellationToken token);
    Task ChangeRevisionAsync(string? revision, CancellationToken token);
    void Toggle(string value);
}
