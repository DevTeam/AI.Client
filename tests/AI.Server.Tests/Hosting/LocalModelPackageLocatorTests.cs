namespace AI.Server.Tests.Hosting;

using AI.Contracts.FileSystem;
using AI.Server.Hosting;
using Moq;
using Xunit;

public sealed class LocalModelPackageLocatorTests
{
    [Theory]
    [InlineData("src/AI.Desktop/bin/Debug/net11.0", true, false, true, true)]
    [InlineData("src/AI.Desktop/bin/Debug/net11.0", true, true, true, false)]
    [InlineData("src/AI.Desktop/bin/Debug/net11.0", false, false, true, false)]
    [InlineData("src/AI.Desktop/bin/Debug/net11.0", true, false, false, false)]
    [InlineData("tests/AI.Server.Tests/bin/Debug/net11.0", true, false, true, false)]
    [InlineData("artifacts/run-desktop", true, false, true, false)]
    public async Task OnlySourceDesktopReusesAnExistingPreparedCache(string relativeOutput, bool allowDevelopment,
        bool installed, bool cached, bool usesCache)
    {
        var root = Path.Combine(Path.GetTempPath(), "local-ai-location-tests");
        var output = Path.Combine(root, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
        var installation = Path.Combine(output, "local-ai");
        var cache = Path.Combine(root, "artifacts", "local-ai", "qwen3-q4-k-m-b12244", "win-x64");
        var known = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(root, "AI.slnx") };
        if (installed) known.Add(Path.Combine(installation, "manifest.json"));
        if (cached) known.Add(Path.Combine(cache, "manifest.json"));
        var files = new Mock<IFileSystem>(MockBehavior.Strict);
        files.Setup(item => item.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) => Task.FromResult(known.Contains(path)));
        var locator = new LocalModelPackageLocator(files.Object, new SystemPath());
        var result = await locator.FindAsync(installation, allowDevelopment ? output : null,
            "win-x64", TestContext.Current.CancellationToken);
        Assert.Equal(usesCache ? cache : installation, result);
    }
}
