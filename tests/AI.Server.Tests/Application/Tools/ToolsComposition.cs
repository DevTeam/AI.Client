// DI guide: [Pure.DI conventions](../../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Application.Tests.Tools;

using System.Diagnostics;
using AI.Contracts.Tools;
using Pure.DI;

/// <summary>
/// Builds the tool presentation services from the same setup Host and Web link in, so the tests
/// exercise the adapter set and ordering the containers really produce.
/// </summary>
internal sealed partial class ToolsComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Root<IToolPresentations>(nameof(Presentations))
            .Root<IToolResultModelProjector>(nameof(ModelProjector))
            .Root<IToolResultCodec>(nameof(Codec));
}
