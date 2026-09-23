namespace AI.Client.Web.Tests.Notifications;

using AI.Client.Web.Notifications;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

public class NotificationServiceTests
{
    private sealed class RecordingPublisher : IUnreadCountPublisher
    {
        public List<int> Counts { get; } = [];

        public Task PublishAsync(int count)
        {
            Counts.Add(count);
            return Task.CompletedTask;
        }
    }

    private sealed class StorageJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = (string)args![0]!;
            if (identifier == "localStorage.getItem")
                return ValueTask.FromResult((TValue)(object?)Items.GetValueOrDefault(key)!);
            if (identifier == "localStorage.setItem") Items[key] = (string)args[1]!;
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    [Fact]
    public async Task DismissingPopupKeepsHistoryAcrossReload()
    {
        var js = new StorageJsRuntime();
        using var first = new NotificationService(js);
        await first.InitializeAsync();
        first.ShowSuccess("Project saved.");
        first.ShowError("Connection failed.");
        first.Dismiss();

        first.Current.ShouldBeNull();
        first.History.Select(item => item.Message).ShouldBe(["Connection failed.", "Project saved."]);

        using var reloaded = new NotificationService(js);
        await reloaded.InitializeAsync();
        reloaded.History.Select(item => item.Message).ShouldBe(["Connection failed.", "Project saved."]);
    }

    [Fact]
    public async Task ResolvingAttentionKeepsEventButRemovesItFromUnreadCount()
    {
        using var service = new NotificationService(new StorageJsRuntime());
        await service.InitializeAsync();
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        service.ShowChatEvent("Tool approval needed", NotificationKind.Info, projectId, chatId, branchId, requiresAction: true);
        service.UnreadCount.ShouldBe(1);
        service.ResolveChatAttention(chatId, branchId);

        service.UnreadCount.ShouldBe(0);
        service.History.Single().IsResolved.ShouldBeTrue();
        service.History.Single().RequiresAction.ShouldBeTrue();
    }

    [Fact]
    public async Task DesktopBadgeFollowsUnreadChangesWithoutCountingGenericToasts()
    {
        using var inner = new NotificationService(new StorageJsRuntime());
        var publisher = new RecordingPublisher();
        using var service = new DesktopBadgeNotificationService(inner, publisher);
        await service.InitializeAsync();
        await service.InitializeAsync();
        service.ShowSuccess("Project saved.");

        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        service.ShowChatEvent("Response ready", NotificationKind.Success, projectId, chatId, branchId);
        service.MarkSeen(service.History[0].Id);

        publisher.Counts.ShouldBe([0, 1, 0]);
    }
}
