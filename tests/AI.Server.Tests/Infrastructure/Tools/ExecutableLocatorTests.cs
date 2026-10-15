namespace AI.Infrastructure.Tests.Tools;

using AI.Contracts.FileSystem;
using AI.Infrastructure.Tools;
using Shouldly;
using Xunit;

public sealed class ExecutableLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "locator-" + Guid.NewGuid().ToString("N"));
    private readonly string _first;
    private readonly string _second;
    private readonly string _work;

    public ExecutableLocatorTests()
    {
        _first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        _second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        _work = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task ShouldFindABareNameInTheFirstPathEntryThatHasIt()
    {
        var program = OperatingSystem.IsWindows() ? "tool.exe" : "tool";
        await File.WriteAllTextAsync(Path.Combine(_second, program), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_work, program), "", TestContext.Current.CancellationToken);
        var locator = Locator($"relative{Path.PathSeparator}{_first}{Path.PathSeparator}\"{_second}\"");

        (await locator.LocateAsync("tool", _work, TestContext.Current.CancellationToken))
            .ShouldBe(Path.Combine(_second, program));
        (await locator.LocateAsync("missing", _work, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await locator.LocateAsync("." + Path.DirectorySeparatorChar + program, _work, TestContext.Current.CancellationToken))
            .ShouldBe(Path.Combine(_work, program));
        (await locator.LocateAsync("." + Path.DirectorySeparatorChar + program, null, TestContext.Current.CancellationToken))
            .ShouldBeNull();
        (await locator.LocateAsync(Path.Combine(_work, program), null, TestContext.Current.CancellationToken))
            .ShouldBe(Path.Combine(_work, program));
    }

    private static ExecutableLocator Locator(string searchPath) =>
        new(new SystemFileSystem(), new SystemPath()) { SearchPath = () => searchPath };
}
