namespace AI.Web.Composer;

using AI.Web.Runs;

/// <summary>The answer a reply would be written to: the head of a branch that ends with one.</summary>
public sealed record ReplySuggestionTarget(Guid ProjectId, Guid ChatId, Guid BranchId, Guid LeafMessageId);

/// <summary>
/// The Host's draft of the user's reply to the answer on screen, which the composer shows as grey
/// text while it is empty. It follows whatever answer the page points it at and drops the draft the
/// moment that changes, so a late reply for another branch never lands in this one.
/// </summary>
public interface IReplySuggestionState
{
    /// <summary>Raised from a background continuation when <see cref="Text"/> changes.</summary>
    event Action? Changed;

    /// <summary>The draft to show, or null: none yet, none at all, or put aside by the user.</summary>
    string? Text { get; }

    /// <summary>Points the state at an answer; the same target again does nothing.</summary>
    void Follow(ReplySuggestionTarget? target);

    /// <summary>Puts the draft aside until the next answer.</summary>
    void Dismiss();

    /// <summary>Asks the Host to write a draft now; false when it has none to offer.</summary>
    Task<bool> RequestAsync();
}

public sealed class ReplySuggestionState(IChatRunsApi runs) : IReplySuggestionState, IDisposable
{
    private ReplySuggestionTarget? _target;
    private string? _text;
    private bool _dismissed;
    private CancellationTokenSource? _cancellation;

    public event Action? Changed;

    public string? Text => _dismissed ? null : _text;

    public void Follow(ReplySuggestionTarget? target)
    {
        if (target == _target) return;
        _target = target;
        _dismissed = false;
        var hadText = _text is not null;
        _text = null;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        if (hadText) Changed?.Invoke();
        // Only waits for a draft the Host started when the answer landed; writes nothing new.
        if (target is not null) _ = LoadAsync(target, generate: false);
    }

    public void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        if (_text is not null) Changed?.Invoke();
    }

    public Task<bool> RequestAsync()
    {
        if (_target is not { } target) return Task.FromResult(false);
        _dismissed = false;
        if (_text is not null)
        {
            Changed?.Invoke();
            return Task.FromResult(true);
        }
        return LoadAsync(target, generate: true);
    }

    private async Task<bool> LoadAsync(ReplySuggestionTarget target, bool generate)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        var cancellation = _cancellation = new CancellationTokenSource();
        try
        {
            var suggestion = await runs.GetReplySuggestionAsync(target.ProjectId, target.ChatId, target.BranchId,
                target.LeafMessageId, generate, cancellation.Token);
            if (_target != target || cancellation.IsCancellationRequested) return false;
            _text = suggestion?.Text;
            Changed?.Invoke();
            return _text is not null;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // A draft is a convenience; without one the composer is simply empty.
            return false;
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
