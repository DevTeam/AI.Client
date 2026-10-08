namespace AI.Infrastructure.Tests.Tools;

using AI.Contracts.FileSystem;
using AI.Mcp.BuiltIn.Grants;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// The catching test for the containment risk the audit names (7.1 #13): a link planted inside a
/// granted root that points outside it must be refused. The link is answered by the file-system
/// contract, so the walk is exercised on every machine and needs no privilege to create a real
/// junction - which matters, because the on-disk catching test for this risk skips wherever a
/// junction cannot be made.
/// </summary>
public sealed class PathGuardLinkTests
{
    private const string Granted = @"C:\data\granted";
    private const string Outside = @"C:\data\outside";

    /// <summary>
    /// The risk made concrete: a path that looks like a child of the grant, reached through a link
    /// that resolves outside it. A guard that canonicalized the path as text would answer with the
    /// granted root and hand out a directory the session was never given.
    /// </summary>
    [Fact]
    public void AFileReachedThroughALinkOutOfTheGrantShouldBeRefused()
    {
        // Given a grant on a directory and a link inside it that resolves elsewhere,
        var files = AnswersLinksResolvingOnly(@"C:\data\granted\escape", Outside);
        var guard = NewGuard(files, Granted);

        // When a path through the link is resolved,
        var error = Should.Throw<GrantException>(() =>
            guard.Resolve(@"C:\data\granted\escape\secret.txt", GrantCapability.Read));

        // Then it is refused: the link is followed, so the escape is visible to containment instead
        // of looking like an ordinary child of the grant.
        error.Message.ShouldContain("No directory grant allows 'read' access to C:\\data\\outside\\secret.txt");
    }

    /// <summary>
    /// The other half of the same decision: a reparse point that resolves to itself - a cloud-sync
    /// placeholder - is not a link, so a path through it stays inside the grant and is allowed. This
    /// is what stops the guard from refusing legitimate paths merely for carrying the attribute.
    /// </summary>
    [Fact]
    public void APlaceholderResolvingToItselfShouldStayInsideTheGrant()
    {
        // Given a grant and an entry that carries a reparse point but resolves to its own path,
        var files = AnswersLinksResolvingOnly(link: null, target: null);
        var guard = NewGuard(files, Granted);

        // When a path below it is resolved,
        var resolved = guard.Resolve(@"C:\data\granted\placeholder\file.txt", GrantCapability.Read);

        // Then it is allowed and canonical, because nothing was followed.
        resolved.ShouldBe(@"C:\data\granted\placeholder\file.txt");
    }

    /// <summary>A link that leads back inside the grant is not an escape and must not be refused.</summary>
    [Fact]
    public void ALinkStayingInsideTheGrantShouldBeAllowed()
    {
        // Given a link inside the grant that resolves to another place inside it,
        var files = AnswersLinksResolvingOnly(@"C:\data\granted\shortcut", @"C:\data\granted\real");
        var guard = NewGuard(files, Granted);

        // When a path through it is resolved,
        var resolved = guard.Resolve(@"C:\data\granted\shortcut\file.txt", GrantCapability.Read);

        // Then it is allowed and rewritten to where the bytes are.
        resolved.ShouldBe(@"C:\data\granted\real\file.txt");
    }

    private static PathGuard NewGuard(Mock<IFileSystem> files, string granted) =>
        new(new Grants(new DirectoryGrantSpec(granted, true, new HashSet<GrantCapability> { GrantCapability.Read })),
            new SystemPath(), files.Object);

    /// <summary>
    /// A contract whose resolver answers every path with itself except one, which answers
    /// <paramref name="target"/> - the link. Passing null for both means nothing is a link.
    /// </summary>
    private static Mock<IFileSystem> AnswersLinksResolvingOnly(string? link, string? target)
    {
        var files = new Mock<IFileSystem>();
        files.Setup(item => item.ResolveLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => path);
        if (link is not null && target is not null)
        {
            files.Setup(item => item.ResolveLinkTargetAsync(link, It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);
        }

        return files;
    }

    private sealed class Grants(params DirectoryGrantSpec[] grants) : IGrantSource
    {
        public IReadOnlyList<DirectoryGrantSpec> Load() => grants;
    }
}
