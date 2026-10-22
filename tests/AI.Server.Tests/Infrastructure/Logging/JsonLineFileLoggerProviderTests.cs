namespace AI.Infrastructure.Tests.Logging;

using AI.Application.Users;
using AI.Contracts.FileSystem;
using AI.Domain.Users;
using AI.Infrastructure.Logging;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Server.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using System.Text.Json;
using Xunit;

[Trait("Category", "Integration")]
public sealed class JsonLineFileLoggerProviderTests
{
    private static readonly Action<ILogger, Guid, int, Exception?> LogChunk =
        LoggerMessage.Define<Guid, int>(LogLevel.Information, new EventId(1002, "ContentChunkReceived"),
            "Chunk OperationId={OperationId} ContentLength={ContentLength}");

    [Fact]
    public void ShouldWriteStructuredJsonLineWithoutMessageContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "AI.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new JsonLineFileLoggerProvider(root, new SystemFileSystem());
            var logger = provider.CreateLogger("Streaming");

            LogChunk(logger, Guid.Empty, 42, null);

            var paths = Directory.GetFiles(Path.Combine(root, "logs"), "*.jsonl");
            paths.Length.ShouldBe(1);
            var path = paths[0];
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            document.RootElement.GetProperty("EventName").GetString().ShouldBe("ContentChunkReceived");
            document.RootElement.GetProperty("Properties").GetProperty("ContentLength").GetInt32().ShouldBe(42);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ShouldLabelEachEntryWithTheUserItWasWrittenFor()
    {
        var files = new MemoryFileSystem();
        var users = new AmbientUserContext();
        using var provider = new JsonLineFileLoggerProvider(
            new InstanceStorageLocation(new ServerOptions("data", null, true)), files, users);
        var logger = provider.CreateLogger("Runs");

        LogChunk(logger, Guid.Empty, 1, null);
        using (users.Enter(UserId.Local)) LogChunk(logger, Guid.Empty, 2, null);

        var entries = files.Files.Single(item => item.Key.EndsWith(".jsonl", StringComparison.Ordinal)).Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        entries[0].GetProperty("UserId").ValueKind.ShouldBe(JsonValueKind.Null);
        entries[1].GetProperty("UserId").GetString().ShouldBe(UserId.Local.ToString());
    }
}
