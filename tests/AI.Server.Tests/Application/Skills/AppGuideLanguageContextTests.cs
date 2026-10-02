namespace AI.Application.Tests.Skills;

using System.Text.Json;
using AI.Application.Skills;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using Shouldly;
using Xunit;

public sealed class AppGuideLanguageContextTests
{
    [Fact]
    public void ShouldCarryTheVisibleBranchUserLanguageInsteadOfTheEnglishAssistantOrAnotherBranch()
    {
        var chatId = Guid.NewGuid();
        var mainUser = Message("User", "Explain this project");
        var branchUser = Message("User", "Покажи, как работать с ветками", mainUser.Id);
        var branchAssistant = Message("Assistant", "Here is an English answer", branchUser.Id);
        var mainAssistant = Message("Assistant", "A newer English answer", mainUser.Id);
        var branchId = Guid.NewGuid();
        var chat = Chat(chatId, [mainUser, branchUser, branchAssistant, mainAssistant],
            [new(chatId, mainAssistant.Id, "Main"), new(branchId, branchAssistant.Id, "Branch")]);
        var context = new AppGuideLanguageContext().Create(new(chat.ProjectId, ChatId: chatId, BranchId: branchId, UiLocale: "en-US"), chat);
        var data = Data(context);
        data.GetProperty("lastUserMessage").GetString().ShouldBe(branchUser.Content);
        data.GetProperty("clientLocale").GetString().ShouldBe("en-US");
        context.ShouldContain("Покажи");
        context.ShouldNotContain(mainAssistant.Content);
        context.ShouldNotContain(mainUser.Content);
        var main = new AppGuideLanguageContext().Create(new(chat.ProjectId, ChatId: chatId), chat);
        Data(main).GetProperty("lastUserMessage").GetString().ShouldBe(mainUser.Content);
    }

    [Fact]
    public void ShouldCarryAnExplicitLanguageAndRetainItAheadOfTheConversationAndLocale()
    {
        var context = new AppGuideLanguageContext().Create(new(Guid.NewGuid(), Language: "ru", UiLocale: "en-US"), null);
        Data(context).GetProperty("requestedLanguage").GetString().ShouldBe("ru");
        context.ShouldContain("Priority: requestedLanguage");
        context.ShouldContain("every navigation comment, question, option, explanation and completion");
    }

    [Fact]
    public void ShouldUseTheClientLocaleWithoutImportingTheHiddenServiceChat()
    {
        var chatId = Guid.NewGuid();
        var launch = Message("User", "Run the built-in application guide in this hidden service chat");
        var hidden = Chat(chatId, [launch], [new(chatId, launch.Id, "Guide")]) with { IsGuide = true };
        var context = new AppGuideLanguageContext().Create(new(hidden.ProjectId, UiLocale: "ru-RU"), hidden);
        Data(context).GetProperty("lastUserMessage").ValueKind.ShouldBe(JsonValueKind.Null);
        Data(context).GetProperty("clientLocale").GetString().ShouldBe("ru-RU");
        context.ShouldNotContain(launch.Content);
    }

    [Fact]
    public void ShouldBoundTheSampleAndPreserveALanguagePreferenceAtTheEnd()
    {
        var chatId = Guid.NewGuid();
        var user = Message("User", "Начало запроса " + new string('x', 5000) + " Отвечай по-русски.");
        var chat = Chat(chatId, [user], [new(chatId, user.Id, "Main")]);
        var context = new AppGuideLanguageContext().Create(new(chat.ProjectId), chat);
        var sample = Data(context).GetProperty("lastUserMessage").GetString();
        sample.ShouldNotBeNull().Length.ShouldBeLessThan(2100);
        sample.ShouldStartWith("Начало запроса");
        sample.ShouldEndWith("Отвечай по-русски.");
    }

    private static JsonElement Data(string context)
    {
        using var document = JsonDocument.Parse(context[context.IndexOf('{')..]);
        return document.RootElement.Clone();
    }

    private static ChatMessageView Message(string role, string content, Guid? parent = null) =>
        new(Guid.NewGuid(), parent, role, content, DateTimeOffset.UtcNow);

    private static ChatDetails Chat(Guid id, IReadOnlyList<ChatMessageView> messages, IReadOnlyList<ChatBranchView> branches) =>
        new(id, Guid.NewGuid(), "Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, null, messages, branches);
}
