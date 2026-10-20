using Pure.DI;
using static Pure.DI.CompositionKind;
using static Pure.DI.Lifetime;

namespace Lib;

internal class Composition
{
    private static void Setup() => DI.Setup(kind: Internal)
        .Bind().To<Log<TT>>()
        .Bind().As(Singleton).To<ConsoleAdapter>();
}
