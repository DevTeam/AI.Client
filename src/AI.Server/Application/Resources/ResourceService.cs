namespace AI.Application.Resources;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Skills;
using AI.Contracts.Resources;

/// <summary>
/// Validates path references against a project's read grants at creation and submission, and the
/// skill a message invokes against the skills enabled in that project. At submission it also takes
/// the snapshots a message keeps: the chosen lines of a file and the uncommitted changes of a
/// directory, so the model later sees what the user saw when sending.
/// </summary>
public sealed class ResourceService(IProjectService projects, IDirectoryBrowser browser,
    IResourceRepository repository, IReviewService reviews, IProjectPathAccess access, ISkillCatalog skills,
    IChatService chats, IWorkspaceDiffReader diffs, IFileExcerptReader excerpts,
    IResourceAssetService assets) : IResourceService
{
    /// <summary>Kinds a message names by id or path without a saved project resource behind them.</summary>
    private static readonly ChatResourceKind[] MessageOnlyKinds =
        [ChatResourceKind.Review, ChatResourceKind.Skill, ChatResourceKind.Chat, ChatResourceKind.Project,
            ChatResourceKind.Diff, ChatResourceKind.Image];

    /// <summary>The most a workspace file sent with its content may hold: as much as an uploaded one.</summary>
    private const int ContentLimit = 15 * 1024 * 1024;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<ChatResource> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken)
    {
        if (kind is not (ChatResourceKind.File or ChatResourceKind.Directory))
            throw new ArgumentException("Only files and directories can be created from a path.");
        var reference = new ChatResource(Guid.CreateVersion7(), kind, path);
        var prepared = (await ValidatePathsAsync(projectId, [reference], cancellationToken))[0];
        return (await repository.GetOrCreateAsync(projectId, prepared, cancellationToken)).Reference;
    }

    public Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        repository.ListAsync(projectId, cancellationToken);

    public Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision,
        CancellationToken cancellationToken) => repository.RetireAsync(projectId, id, expectedRevision, cancellationToken);

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await repository.DeleteProjectAsync(projectId, cancellationToken);
        await assets.DeleteProjectAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceDiffSource>> ListDiffSourcesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        var candidates = new List<(string Path, string Name, string Location)>();
        foreach (var grant in project.DirectoryGrants.Where(grant => grant.ToolNames.Contains("read", StringComparer.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try { root = access.ResolveLinks(grant.CanonicalRoot); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { continue; }
            if (!Directory.Exists(root)) continue;
            var grantName = grant.DisplayName is { Length: > 0 } displayName ? displayName : DirectoryName(root);
            if (diffs.FindRepository(root) is not null) Add(root, grantName, grantName);
            foreach (var nested in diffs.FindNestedRepositories(root))
                Add(nested, DirectoryName(nested),
                    $"{grantName}/{Path.GetRelativePath(root, nested).Replace(Path.DirectorySeparatorChar, '/')}");
        }
        // One git status per repository, all at once: a large work tree must not hold up the rest.
        var counted = await Task.WhenAll(candidates.Select(candidate =>
            Task.Run(() => (candidate, Changed: diffs.ChangedFiles(candidate.Path).Count), cancellationToken)));
        var result = counted
            .Where(item => item.Changed > 0)
            .Select(item => new WorkspaceDiffSource(item.candidate.Path, item.candidate.Name, item.Changed, item.candidate.Location))
            .ToArray();
        // Two checkouts of one name would be two identical links: the location tells them apart.
        return result
            .Select(item => result.Count(other => string.Equals(other.Name, item.Name, StringComparison.OrdinalIgnoreCase)) > 1
                ? item with { Name = item.Location ?? item.Name } : item)
            .ToArray();

        void Add(string path, string name, string location)
        {
            if (!candidates.Any(item => string.Equals(item.Path, path, PathComparison))) candidates.Add((path, name, location));
        }
    }

    private static string DirectoryName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path;

    public async Task<IReadOnlyList<ChatResource>> ValidateAsync(Guid projectId,
        IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken)
    {
        if (references?.Any(item => item.AssetId is not null) == true)
            throw new ArgumentException("Uploaded files belong to chat messages, not project path resources.");
        if (references?.Any(item => item.Kind == ChatResourceKind.Review) == true)
            throw new ArgumentException("Chat ID is required for review references.");
        if (references?.Any(item => item.Kind == ChatResourceKind.Skill) == true)
            throw new ArgumentException("Skills are invoked by a chat message, not saved as project resources.");
        if (references?.Any(item => MessageOnlyKinds.Contains(item.Kind)) == true)
            throw new ArgumentException("Chats, projects and changes are named by a chat message, not saved as project resources.");
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

    public async Task<IReadOnlyList<ChatResource>> ValidateForChatAsync(Guid projectId, Guid chatId,
        IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken)
    {
        if (references is null or { Count: 0 }) return [];
        if (references.Count > 20 || references.Select(item => item.Id).Distinct().Count() != references.Count)
            throw new ArgumentException("A message needs at most 20 unique resources.");
        if (references.Any(item => item.Mention is { } mention && (mention.Length is < 2 or > 400 || mention[0] != '@')))
            throw new ArgumentException("A resource mention is the \"@\" link as written in the message, at most 400 characters.");
        var files = await ValidateAsync(projectId,
            references.Where(item => !MessageOnlyKinds.Contains(item.Kind) && item.AssetId is null).ToArray(),
            cancellationToken);
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
            byId.Add(reference.Id, new ChatResource(review.Id, ChatResourceKind.Review, string.Empty,
                review.Name, review.Kind, Mention: reference.Mention));
        }
        foreach (var reference in references.Where(item => item.Kind == ChatResourceKind.Image
                     || item.Kind == ChatResourceKind.File && item.AssetId is not null))
        {
            if (reference.Id == Guid.Empty || byId.ContainsKey(reference.Id) || reference.AssetId is not { } assetId)
                throw new ArgumentException("Uploaded file reference is invalid or duplicated.");
            var asset = await assets.ReadAsync(projectId, assetId, cancellationToken)
                ?? throw new InvalidOperationException("The attached file is unavailable.");
            var isImage = asset.MediaType is "image/png" or "image/jpeg" or "image/webp" or "image/gif";
            byId.Add(reference.Id, reference with
            {
                Kind = isImage ? ChatResourceKind.Image : ChatResourceKind.File,
                AssetId = assetId.ToLowerInvariant(),
                MediaType = asset.MediaType,
                Size = asset.Data.Length,
                Name = Path.GetFileName(reference.Name ?? "File")
            });
        }
        var invoked = references.Where(item => item.Kind == ChatResourceKind.Skill).ToArray();
        if (invoked.Length > 1) throw new ArgumentException("A message can invoke at most one skill.");
        foreach (var reference in invoked)
        {
            // The effective skill of this project, as app_run_skill would resolve it: a disabled
            // project skill hides an enabled user skill of the same ID rather than falling back.
            var skill = await skills.GetByIdAsync(reference.Path, projectId, cancellationToken);
            if (skill is not { Enabled: true })
                throw new InvalidOperationException($"The skill {reference.Path} is not available in this project.");
            byId.Add(reference.Id, new ChatResource(reference.Id, ChatResourceKind.Skill, skill.Id, skill.Name));
        }
        await AddNamedAsync(projectId, chatId, references, byId, cancellationToken);
        foreach (var file in files)
        {
            // What the client sent is never kept: the excerpt is read here, from the file as it is now.
            byId[file.Id] = file.Lines is { } lines && file.Kind == ChatResourceKind.File
                ? file with { Excerpt = await ReadExcerptAsync(file.Path, lines, cancellationToken), IncludeContent = false }
                : file is { IncludeContent: true, Kind: ChatResourceKind.File }
                    ? await CaptureContentAsync(projectId, file, cancellationToken)
                    : file with { Lines = null, Excerpt = null, IncludeContent = false };
        }
        return references.Select(item => byId[item.Id]).ToArray();
    }

    /// <summary>Chats, projects and uncommitted changes: checked, named, and for changes, captured.</summary>
    private async Task AddNamedAsync(Guid projectId, Guid chatId, IReadOnlyList<ChatResource> references,
        Dictionary<Guid, ChatResource> byId, CancellationToken cancellationToken)
    {
        var named = references
            .Where(item => item.Kind is ChatResourceKind.Chat or ChatResourceKind.Project or ChatResourceKind.Diff)
            .ToArray();
        if (named.Length == 0) return;
        var chatList = named.Any(item => item.Kind == ChatResourceKind.Chat)
            ? (await chats.ListAsync(projectId, cancellationToken)).ToDictionary(item => item.Id)
            : [];
        foreach (var reference in named)
        {
            if (reference.Id == Guid.Empty || byId.ContainsKey(reference.Id))
                throw new ArgumentException("Resource IDs must be valid and unique.");
            switch (reference.Kind)
            {
                case ChatResourceKind.Chat:
                    if (!Guid.TryParse(reference.Path, out var otherChatId) || !chatList.TryGetValue(otherChatId, out var chat))
                        throw new InvalidOperationException("The chat is not in this project.");
                    if (otherChatId == chatId) throw new ArgumentException("A chat cannot reference itself.");
                    byId.Add(reference.Id, new ChatResource(reference.Id, ChatResourceKind.Chat, chat.Id.ToString(), chat.Title,
                        Mention: reference.Mention));
                    break;
                case ChatResourceKind.Project:
                    var project = Guid.TryParse(reference.Path, out var otherProjectId)
                        ? await projects.GetAsync(otherProjectId, cancellationToken) : null;
                    if (project is null) throw new InvalidOperationException("The project does not exist.");
                    byId.Add(reference.Id, new ChatResource(reference.Id, ChatResourceKind.Project, project.Id.ToString(), project.Name,
                        Mention: reference.Mention));
                    break;
                default:
                    byId.Add(reference.Id, await CaptureDiffAsync(projectId, reference, cancellationToken));
                    break;
            }
        }
    }

    private async Task<ChatResource> CaptureDiffAsync(Guid projectId, ChatResource reference, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        if (string.IsNullOrWhiteSpace(reference.Path) || reference.Path.Length > 4096)
            throw new ArgumentException("Resource path is empty or too long.");
        var probe = await browser.ResolveAsync(reference.Path, cancellationToken);
        if (!probe.IsFullyQualified || !probe.DirectoryExists)
            throw new ArgumentException($"The directory does not exist: {reference.Path}");
        var path = access.ResolveLinks(probe.CanonicalPath);
        if (!access.CanRead(project, path)) throw new InvalidOperationException($"No project read grant covers {reference.Path}.");
        if (diffs.FindRepository(path) is null) throw new InvalidOperationException($"{reference.Path} is not in a git repository.");
        var name = reference.Name is { Length: > 0 and <= 200 } given
            ? given : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return new ChatResource(reference.Id, ChatResourceKind.Diff, path, name, Excerpt: diffs.ReadDiff(path),
            Mention: reference.Mention);
    }

    /// <summary>
    /// A workspace file sent with its content: the file as it is now, kept as an asset of the
    /// message like an uploaded one, with its path. An empty file has nothing to send but its path.
    /// </summary>
    private async Task<ChatResource> CaptureContentAsync(Guid projectId, ChatResource file, CancellationToken cancellationToken)
    {
        byte[] data;
        try
        {
            data = await excerpts.ReadAllAsync(file.Path, ContentLimit, cancellationToken);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidOperationException($"{error.Message} Send it as a path instead.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Could not read {file.Path}: {error.Message}");
        }
        if (data.Length == 0) return file with { Lines = null, Excerpt = null, IncludeContent = false };
        var stored = await assets.StoreAsync(projectId, data, Path.GetFileName(file.Path), ChatResourceSource.Workspace,
            file.Path, cancellationToken);
        return file with
        {
            Kind = stored.Kind, Name = stored.Name, Lines = null, Excerpt = null, Source = ChatResourceSource.Workspace,
            AssetId = stored.AssetId, MediaType = stored.MediaType, Size = stored.Size, IncludeContent = false
        };
    }

    private async Task<string> ReadExcerptAsync(string path, ChatLineRange lines, CancellationToken cancellationToken)
    {
        if (lines.Start < 1 || lines.End < lines.Start)
            throw new ArgumentException("A line range starts at 1 and does not end before it starts.");
        try
        {
            return await excerpts.ReadLinesAsync(path, lines.Start, lines.End, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Could not read lines {lines.Start}-{lines.End} of {path}: {error.Message}");
        }
    }

    private async Task<IReadOnlyList<ChatResource>> ValidatePathsAsync(Guid projectId,
        IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken)
    {
        if (references is null or { Count: 0 }) return [];
        if (references.Count > 20) throw new ArgumentException("A message can reference at most 20 resources.");
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        var ids = new HashSet<Guid>();
        var result = new List<ChatResource>(references.Count);
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
            result.Add(reference with { Path = path, Name = null, Source = ChatResourceSource.Workspace,
                AssetId = null, MediaType = null, Size = null });
        }
        return result;
    }
}
