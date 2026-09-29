namespace AI.Application.Tests.Skills;

using AI.Application.Skills;
using Shouldly;
using Xunit;

public class BuiltInSkillCatalogTests
{
    [Fact]
    public void ShouldExposeBundledChatTitleInstructions()
    {
        var catalog = new BuiltInSkillCatalog();

        var skill = catalog.GetById("chat-title");

        skill.ShouldNotBeNull();
        skill.Source.ShouldBe("Built-in");
        skill.Content.ShouldContain("read_chat");
        catalog.List().ShouldContain(item => item.Id == skill.Id);
    }
}
