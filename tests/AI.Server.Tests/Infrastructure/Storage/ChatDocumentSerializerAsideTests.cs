namespace AI.Infrastructure.Tests.Storage;

using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Infrastructure.Storage;
using Shouldly;
using Xunit;

public sealed class ChatDocumentSerializerAsideTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DeliveryAndSenderShouldSurviveARoundTrip()
    {
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Team", Now);
        var question = new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Which port?", Now,
            sender: new ChatMessageSender(chat.Id.Value, Guid.NewGuid(), "question"));
        var aside = new ChatMessage(new ChatMessageId(Guid.NewGuid()), question.Id, ChatMessageRole.User, "Use 8080", Now,
            delivery: ChatMessageDelivery.InTurn);
        chat.AddMessage(question, Now);
        chat.AddMessage(aside, Now);
        var serializer = new ChatDocumentSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(chat, 1)).Chat.Messages;

        restored.Single(message => message.Id == question.Id).Sender.ShouldBe(question.Sender);
        restored.Single(message => message.Id == question.Id).Delivery.ShouldBe(ChatMessageDelivery.Turn);
        restored.Single(message => message.Id == aside.Id).Delivery.ShouldBe(ChatMessageDelivery.InTurn);
        restored.Single(message => message.Id == aside.Id).Sender.ShouldBeNull();
    }

    [Fact]
    public void AnOrdinaryMessageShouldBeWrittenWithoutTheNewFields()
    {
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Plain", Now);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Hello", Now), Now);

        var json = new ChatDocumentSerializer().Serialize(chat, 1);

        json.ShouldNotContain("\"Delivery\"");
        json.ShouldNotContain("\"Sender\"");
    }
}
