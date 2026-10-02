namespace AI.Web.Tests.Updates;

#pragma warning disable CA1859 // Exercise services through their public interfaces.

using AI.Contracts.Updates;
using AI.Web.Updates;
using AI.Web.Notifications;
using AI.Web.State;
using Microsoft.JSInterop;
using Moq;
using Xunit;

public sealed class UpdateClientTests
{
    [Fact]
    public async Task AvailabilityIsNotAnnouncedAgainAfterReload()
    {
        var js = new UpdateJs(new UpdateState("Desktop", "1.0.0", "win-x64", new UpdatePreferences(),
            UpdatePhase.Ready, new UpdateRelease("2.0.0-rc.1", true, "", "", "", "", 1)));
        var notifications = new Mock<INotificationService>();
        var workspace = new Mock<IWorkspaceStateService>();
        using var http = new HttpClient();
        await using (IUpdateClient client = new UpdateClient(http, js, notifications.Object, workspace.Object))
        {
            await client.InitializeAsync();
            await client.ExecuteAsync("check");
        }
        await using (IUpdateClient reloaded = new UpdateClient(http, js, notifications.Object, workspace.Object))
            await reloaded.InitializeAsync();
        notifications.Verify(service => service.ShowUpdate(It.IsAny<string>(), NotificationKind.Info), Times.Once);
    }

    [Fact]
    public async Task RestartFlushesPendingDraftAndFailureRestoresInput()
    {
        var js = new UpdateJs(new UpdateState("Desktop", "1.0.0", "win-x64", new UpdatePreferences(), UpdatePhase.Installing));
        var notifications = new Mock<INotificationService>();
        var workspace = new Mock<IWorkspaceStateService>();
        using var http = new HttpClient();
        await using IUpdateClient client = new UpdateClient(http, js, notifications.Object, workspace.Object);
        await client.InitializeAsync();
        workspace.Verify(service => service.FlushPendingComposerDraftAsync(), Times.Once);
        js.State = js.State with { Phase = UpdatePhase.Failed, Error = "Installation canceled" };
        await client.ExecuteAsync("state");
        Assert.Equal([true, false], js.Frozen);
        notifications.Verify(service => service.ShowUpdate(It.IsAny<string>(), NotificationKind.Error), Times.Once);
    }

    private sealed class UpdateJs(UpdateState state) : IJSRuntime
    {
        public UpdateState State { get; set; } = state;
        public List<bool> Frozen { get; } = [];
        private readonly Dictionary<string, string> _storage = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? value = null;
            switch (identifier)
            {
                case "aiClientUpdates.isDesktop": value = true; break;
                case "aiClientUpdates.request": value = State; break;
                case "localStorage.getItem": value = _storage.GetValueOrDefault((string)args![0]!); break;
                case "localStorage.setItem": _storage[(string)args![0]!] = (string)args[1]!; break;
                case "aiClientUpdates.freeze": Frozen.Add((bool)args![0]!); break;
            }
            return ValueTask.FromResult(value is null ? default! : (TValue)value);
        }
    }
}
