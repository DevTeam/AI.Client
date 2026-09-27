namespace AI.Application.Resources;

using System.Text.RegularExpressions;
using AI.Application.Projects;
using AI.Contracts.Resources;

public sealed partial class WorkspacePathResolver(IProjectService projects, IDirectoryBrowser browser, IProjectPathAccess access)
    : IWorkspacePathResolver
{
    public const int RequestLimit = 100;
    public const int InputLengthLimit = 1_024;

    public async Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count > RequestLimit) throw new ArgumentException($"Resolve at most {RequestLimit} paths at once.");
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        var roots = project.DirectoryGrants
            .Where(grant => grant.ToolNames.Contains("read", StringComparer.OrdinalIgnoreCase))
            .Select(grant => grant.CanonicalRoot)
            .ToArray();
        var result = new List<ResolvedPath>();
        foreach (var input in paths.Where(item => item is not null).Distinct(StringComparer.Ordinal))
        {
            var written = Normalize(input);
            var found = written is null ? null : await FindAsync(project, roots, written, cancellationToken);
            result.Add(found is null ? new ResolvedPath(input, null, null) : found with { Input = input });
        }
        return result;
    }

    /// <summary>
    /// An absolute path is checked where it is; a relative one is tried under each readable root in
    /// grant order, and the first that exists and stays inside a grant wins. Only an absolute path
    /// may come back unreadable: a relative one that climbs out of every root names nothing here.
    /// </summary>
    private async Task<ResolvedPath?> FindAsync(Contracts.Projects.ProjectDetails project, IReadOnlyList<string> roots,
        string written, CancellationToken cancellationToken)
    {
        var absolute = Path.IsPathFullyQualified(written);
        IEnumerable<string> candidates = absolute ? [written] : roots.Select(root => Path.Combine(root, written));
        foreach (var candidate in candidates)
        {
            try
            {
                var probe = await browser.ResolveAsync(candidate, cancellationToken);
                if (!probe.IsFullyQualified || !probe.FileExists && !probe.DirectoryExists) continue;
                var path = access.ResolveLinks(probe.CanonicalPath);
                var kind = probe.FileExists ? ChatResourceKind.File : ChatResourceKind.Directory;
                var allowed = access.AccessOf(project, path);
                if (allowed != PathAccess.None || absolute) return new ResolvedPath(written, path, kind, allowed);
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException
                                              or NotSupportedException)
            {
                // A spelling the file system rejects names nothing; the next root may still match.
            }
        }
        return null;
    }

    /// <summary>
    /// The path part of what a message wrote: quotes and a leading "./" dropped, and a line or
    /// column suffix — "src/app.cs:42", "src/app.cs:42:7", "src/app.cs#L42" — cut off.
    /// </summary>
    private static string? Normalize(string input)
    {
        var text = input.Trim().Trim('"', '\'', '`').Trim();
        text = LineSuffix().Replace(text, string.Empty);
        if (text.StartsWith("./", StringComparison.Ordinal) || text.StartsWith(".\\", StringComparison.Ordinal))
            text = text[2..];
        return text.Length is 0 or > InputLengthLimit || text.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ? null : text;
    }

    [GeneratedRegex(@"(?:#L\d+(?:-L?\d+)?|:\d+(?::\d+)?)$")]
    private static partial Regex LineSuffix();
}
