// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
namespace AI.Search.Configuration;

using System.Diagnostics;
using AI.Contracts.FileSystem;
using Ignoring;
using Matchers;
using Pure.DI;
using Scanning;
using Walking;

/// <summary>Shared registrations linked by consumers and reused through <c>DependsOn</c>.</summary>
internal sealed class Composition
{
    [Conditional("DI")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The composition uses instance methods by design.")]
    private void Setup() => DI.Setup(kind: CompositionKind.Internal)
        .Hint(Hint.Comments, "Off")
        // Every service is stateless and a search keeps nothing between requests: the walker, the
        // scanner and the plan factory can be created per injection, and one search still shares
        // exactly one instance of each through its own graph.
        .Transient<SystemFileSystem, SystemPath, GlobSetFactory, SearchPlanFactory, IgnoreRuleProvider, FileWalker,
            FileScanner, SearchEngine>();
}
