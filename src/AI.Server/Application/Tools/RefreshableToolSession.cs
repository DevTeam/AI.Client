namespace AI.Application.Tools;

using Contracts.Tools;

/// <summary>Reopens the tool connections when a project's directory grants change during a run.</summary>
internal sealed class RefreshableToolSession(
    IToolSessionFactory factory,
    IToolSession initial,
    IReadOnlyList<ToolDirectoryGrant> grants,
    IReadOnlySet<Guid> servers,
    ToolRunContext run) : IToolSession
{
    private IToolSession _current = initial;
    private IReadOnlyList<ToolDirectoryGrant> _grants = grants;

    public IReadOnlyList<AgentTool> Tools => _current.Tools;

    public string ValidateArguments(AgentTool tool, string arguments) =>
        _current.ValidateArguments(tool, arguments);

    public Task<ToolCallResult> CallAsync(AgentTool tool, string arguments,
        IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
        _current.CallAsync(tool, arguments, progress, cancellationToken);

    public async Task<bool> RefreshAsync(IReadOnlyList<ToolDirectoryGrant> grants, CancellationToken cancellationToken)
    {
        if (SameGrants(_grants, grants)) return false;
        var replacement = await factory.OpenAsync(grants, servers, run, cancellationToken);
        var previous = _current;
        _current = replacement;
        _grants = grants;
        await previous.DisposeAsync();
        return true;
    }

    public ValueTask DisposeAsync() => _current.DisposeAsync();

    private static bool SameGrants(IReadOnlyList<ToolDirectoryGrant> left, IReadOnlyList<ToolDirectoryGrant> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Root, pair.Second.Root, StringComparison.OrdinalIgnoreCase)
            && pair.First.Recursive == pair.Second.Recursive
            && pair.First.Capabilities.SequenceEqual(pair.Second.Capabilities, StringComparer.Ordinal));
}
