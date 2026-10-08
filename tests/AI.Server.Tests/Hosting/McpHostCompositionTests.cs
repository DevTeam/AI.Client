using AI.Contracts.FileSystem;
namespace AI.Infrastructure.Tests.Hosting;

using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// Resolves the file-system contract from each MCP host's own composition root by running the host
/// the project actually ships, the same way the server starts it.
/// </summary>
/// <remarks>
/// These two hosts cannot be checked the way the others are. Their setups live in their own
/// executables as <c>internal</c> with no test visibility, and their graphs reach members that are
/// internal to their assembly, so neither linking the setup nor resolving it from the test assembly
/// compiles. Running the host is the honest substitute and the stronger statement: opening a session
/// makes the host build its whole graph, and every tool factory in it takes <see cref="IFileSystem"/>
/// and <see cref="IPath"/>, so a session that lists tools is a graph that resolved the contract.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class McpHostCompositionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBuiltInHostResolvesTheFileSystemContractItsToolFactoriesTake()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "mcp");
        await using var session = await OpenAsync("mcp", directory, Token);

        // Every tool factory is constructed to answer this, and each one takes IFileSystem and IPath.
        session.Tools.ShouldNotBeEmpty();
        session.Tools.ShouldContain(tool => tool.OriginalName == "read_text_file");
    }

    [Fact]
    public async Task TheCSharpHostResolvesTheFileSystemContractItsScriptRunnerTakes()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "mcp-csharp");
        await using var session = await OpenAsync("mcp-csharp", directory, Token);

        var tool = session.Tools.ShouldHaveSingleItem();
        tool.OriginalName.ShouldBe("cs_run");

        // The call reaches ScriptRunner, which takes the contract; a graph that could not resolve it
        // would not have produced this tool, and the value proves the runner really ran.
        var result = await session.CallAsync(tool, "{\"code\":\"40 + 2\"}", null, Token);
        result.IsError.ShouldBeFalse();
        result.StructuredContent!.Value.GetProperty("returnValue").GetString().ShouldBe("42");
    }

    private static async Task<IToolSession> OpenAsync(string folder, string directory, CancellationToken token) =>
        await Factory().OpenAsync(Server(folder, directory), token);

    private static McpServerSettings Server(string folder, string directory) =>
        new(Guid.NewGuid(), folder, "Stdio", true, "Ask", null,
            "AI.Mcp." + (folder == "mcp" ? "BuiltIn" : "CSharp") + (OperatingSystem.IsWindows() ? ".exe" : ""),
            [], directory, [], false);

    private static ExternalToolSessionFactory Factory() =>
        new(Mock.Of<IGlobalSecretStore>(), new SystemFileSystem(), new SystemPath(),
            new ToolResultModelProjector());
}
