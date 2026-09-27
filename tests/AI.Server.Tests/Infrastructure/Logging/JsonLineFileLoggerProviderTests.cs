namespace AI.Infrastructure.Tests.Logging;

using AI.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Shouldly;
using System.Text.Json;
using Xunit;

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
            using var provider = new JsonLineFileLoggerProvider(root);
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
}
