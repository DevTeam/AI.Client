namespace AI.Application.Resources;

using AI.Application.Projects;
using AI.Contracts.Resources;

/// <summary>Validates path references against a project's read grants at creation and submission.</summary>
public sealed class ResourceService(IProjectService projects, IDirectoryBrowser browser,
    IResourceRepository repository, IReviewService reviews, IProjectPathAccess access) : IResourceService
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<ChatResourceRef> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken)
    {
        if (kind is not (ChatResourceKind.File or ChatResourceKind.Directory))
            throw new ArgumentException("Only files and directories can be created from a path.");
        var reference = new ChatResourceRef(Guid.CreateVersion7(), kind, path);
        var prepared = (await ValidatePathsAsync(projectId, [reference], cancellationToken))[0];
        return (await repository.GetOrCreateAsync(projectId, prepared, cancellationToken)).Reference;
    }

    public Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        repository.ListAsync(projectId, cancellationToken);

    public Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision,
        CancellationToken cancellationToken) => repository.RetireAsync(projectId, id, expectedRevision, cancellationToken);

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        repository.DeleteProjectAsync(projectId, cancellationToken);

    public async Task<IReadOnlyList<ChatResourceRef>> ValidateAsync(Guid projectId,
        IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken)
    {
        if (references?.Any(item => item.Kind == ChatResourceKind.Review) == true)
            throw new ArgumentException("Chat ID is required for review references.");
        var validated = await ValidatePathsAsync(projectId, references, cancellationToken);
        if (validated.Count == 0) return validated;
        var catalog = (await repository.ListAsync(projectId, cancellationToken))
            .ToDictionary(item => item.Reference.Id);
        foreach (var reference in validated)
        {
            if (!catalog.TryGetValue(reference.Id, out var saved) || saved.Retired
                || saved.Reference.Kind != reference.Kind
                || !string.Equals(saved.Reference.Path, reference.Path, PathComparison))
                throw new InvalidOperationException("Resource reference is missing, retired, or changed.");
        }
        return validated;
    }

    public async Task<IReadOnlyList<ChatResourceRef>> ValidateForChatAsync(Guid projectId, Guid chatId,
        IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken)
    {
        if (references is null or { Count: 0 }) return [];
        if (references.Count > 20 || references.Select(item => item.Id).Distinct().Count() != references.Count)
            throw new ArgumentException("A message needs at most 20 unique resources.");
        var files = await ValidateAsync(projectId,
            references.Where(item => item.Kind != ChatResourceKind.Review).ToArray(), cancellationToken);
        var reviewList = references.Any(item => item.Kind == ChatResourceKind.Review)
            ? (await reviews.ListAsync(projectId, chatId, cancellationToken)).ToDictionary(item => item.Id)
            : [];
        var byId = files.ToDictionary(item => item.Id);
        foreach (var reference in references.Where(item => item.Kind == ChatResourceKind.Review))
        {
            if (!reviewList.TryGetValue(reference.Id, out var review))
                throw new InvalidOperationException("Review resource is not in this chat.");
            if (review.Kind == ChatReviewKind.Diff ? review.Comments.Count == 0
                : review.MessageComments is not { Count: > 0 })
                throw new InvalidOperationException("A review without comments cannot be attached to a message.");
            byId.Add(reference.Id, new ChatResourceRef(review.Id, ChatResourceKind.Review, string.Empty,
                review.Name, review.Kind));
        }
        return references.Select(item => byId[item.Id]).ToArray();
    }

    private async Task<IReadOnlyList<ChatResourceRef>> ValidatePathsAsync(Guid projectId,
        IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken)
    {
        if (references is null or { Count: 0 }) return [];
        if (references.Count > 20) throw new ArgumentException("A message can reference at most 20 resources.");
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        var ids = new HashSet<Guid>();
        var result = new List<ChatResourceRef>(references.Count);
        foreach (var reference in references)
        {
            if (reference.Id == Guid.Empty || !ids.Add(reference.Id) || !Enum.IsDefined(reference.Kind))
                throw new ArgumentException("Resource IDs and kinds must be valid and unique.");
            if (string.IsNullOrWhiteSpace(reference.Path) || reference.Path.Length > 4096)
                throw new ArgumentException("Resource path is empty or too long.");
            var probe = await browser.ResolveAsync(reference.Path, cancellationToken);
            if (!probe.IsFullyQualified || reference.Kind == ChatResourceKind.File && !probe.FileExists
                || reference.Kind == ChatResourceKind.Directory && !probe.DirectoryExists)
                throw new ArgumentException($"The {reference.Kind.ToString().ToLowerInvariant()} does not exist: {reference.Path}");
            var path = access.ResolveLinks(probe.CanonicalPath);
            if (!access.CanRead(project, path)) throw new InvalidOperationException($"No project read grant covers {reference.Path}.");
            result.Add(reference with { Path = path, Name = null });
        }
        return result;
    }
}
