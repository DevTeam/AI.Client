namespace AI.Infrastructure.Tests.Storage;

using AI.Infrastructure.Storage;
using Shouldly;
using Xunit;

public sealed class PhysicalTextFileSystemTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ai-client-fs").FullName;

    public void Dispose()
    {
        foreach (var path in Directory.GetFiles(_directory)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task ARefusedRenameShouldSayWhichFileWasRefused()
    {
        // A save is a write to a temporary file and then this rename, so a directory the application
        // may not write to announces itself here — and "Access to the path is denied" names no path,
        // which left the user with a stack frame and nothing to act on.
        var system = new PhysicalTextFileSystem();
        var source = Path.Combine(_directory, "settings.json.tmp");
        var destination = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(source, "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(destination, "{}", TestContext.Current.CancellationToken);
        File.SetAttributes(destination, FileAttributes.ReadOnly);

        var error = await Should.ThrowAsync<UnauthorizedAccessException>(
            () => system.MoveAsync(source, destination, true, CancellationToken.None));

        error.Message.ShouldContain("settings.json");
        // Callers classify storage failures by exception type, so the type has to survive the retelling.
        error.InnerException.ShouldBeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ARefusedWriteShouldSayWhichFileWasRefused()
    {
        var system = new PhysicalTextFileSystem();
        var path = Path.Combine(_directory, "chat.json");
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        (await Should.ThrowAsync<UnauthorizedAccessException>(
            () => system.WriteTextAsync(path, "{}", CancellationToken.None))).Message.ShouldContain("chat.json");
    }

    [Fact]
    public async Task ASaveShouldLandWhileSomebodyIsReadingTheDocument()
    {
        // The failure this reproduces: four subtasks reading the chat their parent was writing.
        // A reader held the file without sharing delete, Windows refused to rename over it, and the
        // save died with an access denial naming a directory whose permissions were never at fault.
        var system = new PhysicalTextFileSystem();
        var source = Path.Combine(_directory, "chat.json.tmp");
        var destination = Path.Combine(_directory, "chat.json");
        await File.WriteAllTextAsync(destination, "old", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(source, "new", TestContext.Current.CancellationToken);

        await using var reader = new FileStream(destination, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete,
        });
        await system.MoveAsync(source, destination, true, CancellationToken.None);

        (await system.ReadTextAsync(destination, CancellationToken.None)).ShouldBe("new");
    }

    [Fact]
    public async Task ADocumentShouldStillBeReadableWhileASaveOfItIsInFlight()
    {
        // The same sharing seen from the other side: a read must not be refused because a save is
        // holding the file. Every document is written whole, so a reader sees one version or the
        // other and never half of either.
        var system = new PhysicalTextFileSystem();
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{\"a\":1}", TestContext.Current.CancellationToken);

        await using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        (await system.ReadTextAsync(path, CancellationToken.None)).ShouldBe("{\"a\":1}");
    }

    [Fact]
    public async Task ALockThatOutlastsEveryAttemptShouldBeReportedWithItsPath()
    {
        // Reads retry, because replacing a document makes it unopenable for a fraction of a
        // millisecond. Retrying has to end somewhere: a file genuinely held by something else is
        // reported rather than waited on forever, and the report names the file.
        var system = new PhysicalTextFileSystem();
        var path = Path.Combine(_directory, "locked.json");
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);

        await using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        (await Should.ThrowAsync<IOException>(
            () => system.ReadTextAsync(path, CancellationToken.None))).Message.ShouldContain("locked.json");
    }

    [Fact]
    public async Task AMissingDocumentShouldReadAsAbsent()
    {
        var system = new PhysicalTextFileSystem();

        (await new PhysicalTextFileSystem().ReadTextAsync(Path.Combine(_directory, "gone.json"), CancellationToken.None))
            .ShouldBeNull();
        (await system.ReadTextAsync(Path.Combine(_directory, "no", "such", "place.json"), CancellationToken.None))
            .ShouldBeNull();
    }

    [Fact]
    public async Task AnOrdinaryRenameShouldStayOrdinary()
    {
        var system = new PhysicalTextFileSystem();
        var source = Path.Combine(_directory, "project.json.tmp");
        var destination = Path.Combine(_directory, "project.json");
        await File.WriteAllTextAsync(source, "{\"a\":1}", TestContext.Current.CancellationToken);

        await system.MoveAsync(source, destination, true, CancellationToken.None);

        (await system.ReadTextAsync(destination, CancellationToken.None)).ShouldBe("{\"a\":1}");
        File.Exists(source).ShouldBeFalse();
    }
}
