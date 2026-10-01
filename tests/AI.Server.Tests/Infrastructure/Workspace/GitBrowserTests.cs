namespace AI.Infrastructure.Tests.Workspace;

using AI.Infrastructure.Credentials;
using AI.Infrastructure.Workspace;
using Moq;
using Shouldly;
using Xunit;

public sealed class GitBrowserTests
{
    [Fact]
    public void ShouldBrowseRealRefsAndCommitHistoryWithoutChangingTheRepository()
    {
        var repository = Path.Combine(Path.GetTempPath(), "ai-git-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        var runner = new ProcessCommandRunner();
        string Git(params string[] args)
        {
            var result = runner.Run("git", ["-C", repository, .. args]);
            result.ShouldNotBeNull();
            result.ExitCode.ShouldBe(0, result.StandardError);
            return result.StandardOutput.Trim();
        }
        try
        {
            Git("init", "--initial-branch=main");
            Git("-c", "user.name=Picker Test", "-c", "user.email=picker@example.invalid", "commit", "--allow-empty", "-m", "First change");
            var first = Git("rev-parse", "HEAD");
            Git("branch", "topic");
            Git("-c", "user.name=Picker Test", "-c", "user.email=picker@example.invalid", "commit", "--allow-empty", "-m", "Второе изменение");
            var head = Git("rev-parse", "HEAD");
            Git("update-ref", "refs/remotes/origin/main", head);
            Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
            var status = Git("status", "--porcelain=v1");
            var browser = new GitBrowser(runner);

            var branches = browser.Branches(repository);
            branches.Items.Select(item => item.Value).ShouldBe(["refs/heads/main", "refs/heads/topic", "refs/remotes/origin/main"]);
            browser.Commits(repository, "refs/heads/topic", 0).Items.ShouldHaveSingleItem().Value.ShouldBe(first);
            var commits = browser.Commits(repository, null, 0);
            commits.Items.Select(item => item.Value).ShouldBe([head, first]);
            commits.Items[0].Label.ShouldContain("Второе изменение");
            browser.Commits(repository, null, 1).Items.ShouldHaveSingleItem().Value.ShouldBe(first);
            Should.Throw<InvalidOperationException>(() => browser.Commits(repository, "--output=unexpected", 0));
            Git("rev-parse", "HEAD").ShouldBe(head);
            Git("status", "--porcelain=v1").ShouldBe(status);
        }
        finally
        {
            // Git object files are read-only on Windows.
            foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public void ShouldPaginateWithOneLookaheadCommitAndResolveRevisionsBeforeLog()
    {
        var runner = new Mock<ICommandRunner>();
        runner.Setup(item => item.Run("git", It.IsAny<IReadOnlyList<string>>(), null))
            .Returns((string _, IReadOnlyList<string> args, string? _) =>
            {
                if (args.Contains("--show-toplevel")) return new CommandResult(0, Path.GetTempPath(), "");
                if (args.Contains("--verify")) return new CommandResult(0, new string('a', 40), "");
                args.ShouldContain("--skip=100");
                args.ShouldContain("--max-count=101");
                args.ShouldContain(new string('a', 40));
                args.ShouldNotContain("topic");
                return new CommandResult(0, string.Join('\n', Enumerable.Range(0, 101).Select(index => $"{index}\t{index}\tAuthor\t2026-10-01\tChange")), "");
            });
        var listing = new GitBrowser(runner.Object).Commits(Path.GetTempPath(), "topic", 100);
        listing.Items.Count.ShouldBe(100);
        listing.HasMore.ShouldBeTrue();
    }
}
