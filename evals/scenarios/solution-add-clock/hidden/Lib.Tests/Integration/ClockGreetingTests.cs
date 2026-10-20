using Moq;
using Pure.DI;

namespace Lib.Tests.Integration;

public partial class ClockGreetingTests
{
    private readonly Mock<IOutput> _output = new();
    private readonly Mock<IInput> _input = new();
    private readonly Mock<IClock> _clock = new();

    [Fact]
    public void ShouldGreetWithTheTimeOfTheClock()
    {
        _clock.SetupGet(i => i.Now).Returns(new DateTimeOffset(2026, 1, 2, 9, 5, 0, TimeSpan.Zero));

        Service.Run();

        _output.Verify(i => i.WriteLine("Hello! It is 09:05."));
        _output.Verify(i => i.WriteLine("Press the Enter key to exit."));
    }

    private void Setup() =>
        DI.Setup(nameof(ClockGreetingTests))
            .DependsOn("Lib.Composition")
            .Bind<IOutput>().To(_ => _output.Object)
            .Bind<IInput>().To(_ => _input.Object)
            .Bind<IClock>().To(_ => _clock.Object)
            .Root<IService>("Service");
}
