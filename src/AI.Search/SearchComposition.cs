// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
namespace AI.Search;

using System.Diagnostics;
using Pure.DI;

/// <summary>Standalone root built from the same registrations the application links.</summary>
public sealed partial class SearchComposition
{
    [Conditional("DI")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Pure.DI composition uses instance methods.")]
    private void Setup() => DI.Setup()
        .DependsOn("AI.Search.Configuration.Composition")
        .Hint(Hint.Comments, "Off")
        .Hint(Hint.Resolve, "Off")
        .Root<ISearchEngine>(nameof(Engine));
}
