namespace AI.Application.Projects;

/// <summary>
/// The order the person gave the projects, kept apart from the projects themselves: moving a
/// project is not an edit of it and must not change its revision or race an edit in progress.
/// </summary>
public interface IProjectOrderRepository
{
    /// <summary>The project ids first to last; empty while no order was ever given.</summary>
    Task<IReadOnlyList<Guid>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<Guid> projectIds, CancellationToken cancellationToken);
}
