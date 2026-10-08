// DI guide: [Pure.DI conventions](../../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Infrastructure.Tests.Hosting;

using System.Diagnostics;
using AI.Contracts.FileSystem;
using AI.Server.CommandLine;
using Pure.DI;
using Shouldly;
using Xunit;

/// <summary>
/// The graph both executables that host the server build on: <c>AI.Host</c> and <c>AI.Desktop</c>
/// link the command-line composition and take their file system from it, so a contract the command
/// line cannot resolve is a product that does not link.
/// </summary>
/// <remarks>
/// The setup the executables really use is linked from
/// <c>src/AI.Server/CommandLine/Composition.cs</c> rather than copied here, which is the whole point:
/// when that file stopped reaching the setup that binds the file system, the resolution below failed
/// while every test command stayed green, because the test graph supplied its own bindings
/// (<c>tests/AI.Server.Tests/TestServer.cs</c>). Linking the real setup means the same mistake fails
/// the build again instead of hiding behind a test-only binding.
/// </remarks>
internal sealed partial class CommandLineFileSystemComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Server.CommandLine.Composition")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Root<IFileSystem>(nameof(Files))
            .Root<IPath>(nameof(Paths))
            .Root<IAtomicFileWriter>(nameof(Atomic))
            .Root<IServerCommandLine>(nameof(CommandLine));
}

/// <summary>
/// Resolves the file-system contract from each executable's own composition root, so the wiring the
/// executables ship is proven rather than assumed.
/// </summary>
public sealed class ExecutableCompositionTests
{
    [Fact]
    public void TheCommandLineGraphResolvesTheFileSystemContractItsCommandLineNeeds()
    {
        var composition = new CommandLineFileSystemComposition();

        composition.Paths.ShouldBeOfType<SystemPath>();
        composition.Files.ShouldBeOfType<SystemFileSystem>();
        composition.Atomic.ShouldBeOfType<AtomicFileWriter>();
        composition.CommandLine.ShouldBeOfType<ServerCommandLine>();
    }
}
