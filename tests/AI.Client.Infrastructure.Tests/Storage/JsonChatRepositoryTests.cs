namespace AI.Client.Infrastructure.Tests.Storage;

using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Storage;
using Shouldly;
using Xunit;

public sealed class JsonChatRepositoryTests
{
    [Fact]
    public async Task ShouldIgnoreRunDocumentsWhenListingChats()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var fs = new MemoryFileSystem();
        var paths = new ChatStoragePaths("data");
        fs.Files[Path.Combine(paths.GetChatsDirectory(projectId), "chat.run.json")] = "{}";
        using var repository = new JsonChatRepository(fs, paths);
        (await repository.ListAsync(projectId, CancellationToken.None)).ShouldBeEmpty();
    }
}
