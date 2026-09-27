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
            .Hint(Hint.Resolve, "Off")
            .DependsOn("AI.Contracts.Composition")
            .Root<IToolPresentations>("Presentations")
            .Root<IToolResultModelProjector>("ModelProjector")
            .Root<IToolResultCodec>("Codec");
}
