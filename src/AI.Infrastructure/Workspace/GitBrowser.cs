namespace AI.Infrastructure.Workspace;

using System.Globalization;
using AI.Application.Workspace;
using AI.Contracts.Git;
using AI.Infrastructure.Credentials;

/// <summary>Reads local Git data without a shell, fetch or working-tree mutation.</summary>
public sealed class GitBrowser(ICommandRunner commands) : IGitBrowser
{
    private const int PageSize = 100;
    private const int BranchLimit = 2_000;

    public GitListing Branches(string repositoryPath)
    {
        var root = Root(repositoryPath);
        var output = Git(root, "for-each-ref", "--sort=refname", $"--count={BranchLimit + 1}",
            "--format=%(refname)%09%(refname:short)%09%(symref)", "refs/heads/", "refs/remotes/");
        var lines = Lines(output).ToArray();
        var items = lines.Select(line => line.Split('\t')).Where(fields => fields.Length == 3 && fields[2].Length == 0)
            .Select(fields => new GitChoice(fields[0], fields[1], fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal)
                ? "Remote-tracking branch" : "Local branch")).ToArray();
        return new GitListing(root, items.Take(BranchLimit).ToArray(), lines.Length > BranchLimit);
    }

    public GitListing Commits(string repositoryPath, string? revision, int skip)
    {
        if (skip is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(skip));
        var root = Root(repositoryPath);
        // Resolve before log: arbitrary revision text must never become a log option or a pathspec.
        var target = string.IsNullOrWhiteSpace(revision) ? "--all"
            : Git(root, "rev-parse", "--verify", "--end-of-options", revision + "^{commit}").Trim();
        var output = Git(root, "--no-pager", "log", "--date-order", $"--skip={skip.ToString(CultureInfo.InvariantCulture)}",
            $"--max-count={PageSize + 1}", "--format=%H%x09%h%x09%an%x09%aI%x09%s", target, "--");
        var items = Lines(output).Select(line => line.Split('\t', 5)).Where(fields => fields.Length == 5)
            .Select(fields => new GitChoice(fields[0], fields[1] + " " + fields[4], fields[2] + " · " + fields[3])).ToArray();
        return new GitListing(root, items.Take(PageSize).ToArray(), items.Length > PageSize);
    }

    private string Root(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Select an absolute repository directory.", nameof(path));
        return Git(path, "rev-parse", "--show-toplevel").Trim();
    }

    private string Git(string directory, params string[] arguments)
    {
        var result = commands.Run("git", ["-C", directory, "-c", "core.quotepath=off", .. arguments]);
        if (result is not { ExitCode: 0 })
            throw new InvalidOperationException(result?.StandardError.Trim() ?? "Git is not installed on the host.");
        return result.StandardOutput;
    }

    private static IEnumerable<string> Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r'));
}
