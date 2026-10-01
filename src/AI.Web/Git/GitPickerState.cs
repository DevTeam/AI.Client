namespace AI.Web.Git;

using AI.Contracts.Git;

public sealed class GitPickerState(IGitApi api) : IGitPickerState
{
    private readonly List<GitChoice> _items = [];
    private readonly List<string> _values = [];
    // Every label seen while the dialog is open, so a choice keeps its readable name after a
    // different history branch is loaded and the row itself is gone from the list.
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private string _repository = string.Empty;
    private string _kind = "branch";
    private string? _revision;
    private bool _multiple;

    public bool IsLoading { get; private set; }
    public bool HasMore { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string Filter { get; set; } = string.Empty;
    public IReadOnlyList<string> Values => _values;
    public IReadOnlyList<GitChoice> Items => _items.Where(item =>
        (item.Label + " " + item.Description + " " + item.Value).Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray();

    public async Task OpenAsync(string repositoryPath, string kind, bool multiSelect, string? revision,
        IReadOnlyList<string> selected, CancellationToken token)
    {
        _repository = repositoryPath;
        _kind = kind;
        _multiple = multiSelect;
        _revision = revision;
        Filter = string.Empty;
        _items.Clear();
        _labels.Clear();
        _values.Clear();
        _values.AddRange(selected.Distinct(StringComparer.Ordinal).Take(multiSelect ? 200 : 1));
        await LoadMoreAsync(token);
    }

    public async Task LoadMoreAsync(CancellationToken token)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var listing = _kind == "branch" ? await api.BranchesAsync(_repository, token)
                : await api.CommitsAsync(_repository, _revision, _items.Count, token);
            _items.AddRange(listing.Items);
            foreach (var item in listing.Items) _labels[item.Value] = item.Label;
            HasMore = listing.HasMore;
        }
        catch (HttpRequestException error) { ErrorMessage = error.Message; }
        finally { IsLoading = false; }
    }

    public async Task ChangeRevisionAsync(string? revision, CancellationToken token)
    {
        if (IsLoading) return;
        _revision = revision;
        _items.Clear();
        HasMore = false;
        await LoadMoreAsync(token);
    }

    public string LabelOf(string value) => _labels.GetValueOrDefault(value, value);

    public void Clear() => _values.Clear();

    public void Toggle(string value)
    {
        if (_values.Remove(value)) return;
        if (!_items.Any(item => item.Value == value)) return;
        if (!_multiple) _values.Clear();
        if (_values.Count < 200) _values.Add(value);
    }
}
