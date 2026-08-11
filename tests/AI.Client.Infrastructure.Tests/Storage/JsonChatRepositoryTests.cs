using AI.Client.Application.Chats;
using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

namespace AI.Client.Infrastructure.Tests.Storage;

public sealed class JsonChatRepositoryTests
{
    [Fact]
    public async Task ShouldIgnoreRunStateDocumentsWhenListingChats()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var fileSystem = new Mock<ITextFileSystem>(MockBehavior.Strict);
        var paths = new Mock<IChatStoragePaths>(MockBehavior.Strict);
        var serializer = new Mock<IChatDocumentSerializer>(MockBehavior.Strict);
        paths.Setup(item => item.GetChatsDirectory(projectId)).Returns("chats");
        fileSystem.Setup(item => item.ListFilesAsync("chats", "*.json", CancellationToken.None))
            .ReturnsAsync(["chats/chat.run.json"]);

        var repository = new JsonChatRepository(fileSystem.Object, paths.Object, serializer.Object);
        var result = await repository.ListAsync(projectId, CancellationToken.None);

        result.ShouldBeEmpty();
        fileSystem.Verify(item => item.ListFilesAsync("chats", "*.json", CancellationToken.None), Times.Once);
        fileSystem.VerifyNoOtherCalls();
        serializer.VerifyNoOtherCalls();
    }
}
