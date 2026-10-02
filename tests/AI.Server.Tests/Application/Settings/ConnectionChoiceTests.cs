namespace AI.Application.Tests.Settings;

using AI.Application.Settings;
using AI.Contracts.Settings;
using Shouldly;
using Xunit;

public sealed class ConnectionChoiceTests
{
    private static readonly ConnectionSettings Disabled = new(Guid.NewGuid(), "Off", "http://off", "off", false, false, true);
    private static readonly ConnectionSettings Chosen = new(Guid.NewGuid(), "Chosen", "http://chosen", "chosen", true, false, true);
    private static readonly ConnectionSettings Default = new(Guid.NewGuid(), "Default", "http://default", "default", true, true, true);
    private static readonly IReadOnlyList<ConnectionSettings> All = [Disabled, Chosen, Default];

    [Fact]
    public void ShouldPassOverADisabledProjectConnectionAsTheComposerDoes()
    {
        // A chat with no connection of its own in a project left on a disabled one: the composer
        // shows the default, so the request goes there too.
        new ConnectionChoice().Choose(All, null, Disabled.Id).ShouldBe(Default);
    }

    [Fact]
    public void ShouldPreferTheChatsThenTheProjectsEnabledConnection()
    {
        var choice = new ConnectionChoice();

        choice.Choose(All, Chosen.Id, Default.Id).ShouldBe(Chosen);
        choice.Choose(All, Disabled.Id, Chosen.Id).ShouldBe(Chosen);
        choice.Choose(All, null, null).ShouldBe(Default);
    }

    [Fact]
    public void ShouldChooseNothingWhenNoConnectionIsEnabled() =>
        new ConnectionChoice().Choose([Disabled], Disabled.Id, null).ShouldBeNull();
}
