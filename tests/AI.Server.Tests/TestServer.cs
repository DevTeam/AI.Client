// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Server.Tests;

using System.Diagnostics;
using AI.Application.Chat;
using AI.Application.Settings;
using AI.Application.Users;
using AI.Contracts.FileSystem;
using AI.Infrastructure.Tests.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pure.DI;

/// <summary>
/// The real server graph with only its edges replaced: storage in memory, the model endpoint and
/// the secret store supplied by the test, loggers silent. Fixtures build their own compositions from
/// the shipped setups followed by this one, so what they exercise is the wiring the Host ships
/// rather than a hand-kept copy of it. Order matters: a later setup's binding replaces an earlier
/// one, and only in the composition itself — a replacement made inside a setup that depends on
/// the server would be lost.
/// </summary>
internal sealed class TestServer
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            .Hint(Hint.Comments, "Off")
            .Arg<MemoryFileSystem>("fileSystem")
#pragma warning disable DIW000 // These registrations intentionally replace production edge bindings.
            .Arg<IChatCompletionClient>("completion")
            .Singleton(() => Mock.Of<IGlobalSecretStore>())
            // The endpoint's model catalog is the model endpoint too; it lists nothing here.
            .Singleton(() => Mock.Of<IConnectionModelsResolver>(item =>
                item.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())
                    == Task.FromResult<IReadOnlyList<AI.Contracts.Settings.ResolvedModelInfo>>(Array.Empty<AI.Contracts.Settings.ResolvedModelInfo>())))
            // The whole server performs its IO through the one file-system contract, so this single
            // binding puts every repository, the logger and the secret store onto the shared fake.
            .Bind<IFileSystem>().To((MemoryFileSystem fileSystem) => fileSystem)
            // No request enters a user scope here; every call runs for the local user.
            .Bind<IUserContext>().As(Lifetime.Singleton).To<LocalUserContext>()
#pragma warning restore DIW000
            // Outside ASP.NET nothing supplies Microsoft's loggers.
            .Bind<ILogger<TT>>().To(_ => NullLogger<TT>.Instance);
}
