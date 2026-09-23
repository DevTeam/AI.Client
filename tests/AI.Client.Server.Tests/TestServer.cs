// ReSharper disable UnusedMember.Local
namespace AI.Client.Server.Tests;

using System.Diagnostics;
using AI.Client.Application.Chat;
using AI.Client.Application.Settings;
using AI.Client.Infrastructure.Storage;
using AI.Client.Infrastructure.Tests.Storage;
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
            .Arg<MemoryFileSystem>("fileSystem")
            .Bind<ITextFileSystem>().To((MemoryFileSystem fileSystem) => fileSystem)
            .Arg<IChatCompletionClient>("completion")
            .Bind<IGlobalSecretStore>().As(Lifetime.Singleton).To(_ => Mock.Of<IGlobalSecretStore>())
            // Outside ASP.NET nothing supplies Microsoft's loggers.
            .Bind<ILogger<TT>>().To(_ => NullLogger<TT>.Instance);
}
