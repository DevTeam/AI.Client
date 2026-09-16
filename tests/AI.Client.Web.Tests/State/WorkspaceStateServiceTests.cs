namespace AI.Client.Web.Tests.State;

using System.Text.Json;
using AI.Client.Web.State;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

public class WorkspaceStateServiceTests
{
    private const string DraftsStorageKey = "ai-client.composer-drafts.v1";
    private const string HistoryStorageKey = "ai-client.composer-history.v1";

    // Hand-rolled IJSRuntime fake: Moq's strict mode mishandles the InvokeVoidAsync
    // extension method (which the SDK rewrites into InvokeAsync<object>), and we want
    // these tests to pin down behaviour without standing up a JS runtime. Tracking
    // every write lets assertions reason about persistence directly.
    private sealed class FakeJSRuntime : IJSRuntime
    {
        public Dictionary<string, string> Entries { get; } = new();
        public List<(string Identifier, IReadOnlyList<object?> Args)> Calls { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args?.ToArray() ?? []));
            if (identifier == "localStorage.getItem")
            {
                var key = (string)args![0]!;
                var value = Entries.TryGetValue(key, out var v) ? v : null;
                return ValueTask.FromResult((TValue)(object?)value!);
            }

            // setItem (no CancellationToken) and any other write — return a default TValue.
            // We don't actually persist through this path in our test scenarios; the service
            // uses InvokeVoidAsync which the SDK resolves to InvokeAsync<object>.
            Entries[(string)args![0]!] = (string)args[1]!;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args?.ToArray() ?? []));
            if (identifier == "localStorage.setItem")
            {
                Entries[(string)args![0]!] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private static (WorkspaceStateService Service, FakeJSRuntime Js) CreateService()
    {
        var js = new FakeJSRuntime();
        return (new WorkspaceStateService(js), js);
    }

    [Fact]
    public async Task ShouldKeepDraftInMemoryWhenAnotherKeyCancelsTheDebounce()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        // User types in A, then switches to B and types within 500ms — the second queue
        // cancels the first debounce. The previous behaviour would have left A's text
        // entirely outside the dictionary (it was only mutated inside the now-cancelled
        // SaveDraftAfterDelayAsync). After the eager-update fix, both keys are kept.
        service.QueueComposerDraftSave($"new:{projectA}", "Привет");
        service.QueueComposerDraftSave($"new:{projectB}", "World");

        service.GetComposerDraft($"new:{projectA}").ShouldBe("Привет");
        service.GetComposerDraft($"new:{projectB}").ShouldBe("World");
    }

    [Fact]
    public async Task ShouldFlushPendingDraftToLocalStorageWithoutWaitingForDebounce()
    {
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        service.QueueComposerDraftSave($"new:{projectA}", "Привет");

        // Simulate SelectProjectAsync calling flush right after typing — the user has
        // navigated away, so the next render cycle cannot be relied on to fire the
        // debounced write. Persisting now is the whole point of FlushPendingComposerDraftAsync.
        await service.FlushPendingComposerDraftAsync();

        js.Entries.ShouldContainKey(DraftsStorageKey);
        var persisted = JsonSerializer.Deserialize<Dictionary<string, string>>(js.Entries[DraftsStorageKey])!;
        persisted.ShouldContainKey($"new:{projectA}");
        persisted[$"new:{projectA}"].ShouldBe("Привет");
    }

    [Fact]
    public async Task ShouldPersistDraftForPreviousProjectAfterCancellingItsDebounce()
    {
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        // Scenario from the bug report: typing in A, switching to B (cancels A's debounce),
        // typing in B (queues B's debounce), then the project switch flushes everything.
        // The flush must persist BOTH drafts — not just B's.
        service.QueueComposerDraftSave($"new:{projectA}", "Привет");
        service.QueueComposerDraftSave($"new:{projectB}", "World");
        await service.FlushPendingComposerDraftAsync();

        var persisted = JsonSerializer.Deserialize<Dictionary<string, string>>(js.Entries[DraftsStorageKey])!;
        persisted[$"new:{projectA}"].ShouldBe("Привет");
        persisted[$"new:{projectB}"].ShouldBe("World");
    }

    [Fact]
    public async Task ShouldRemoveDraftKeyWhenClearedThenFlushed()
    {
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        service.QueueComposerDraftSave($"new:{projectA}", "Привет");
        await service.FlushPendingComposerDraftAsync();

        // CreateChatAsync queues an empty save to drop the draft before navigating away.
        service.QueueComposerDraftSave($"new:{projectA}", string.Empty);
        await service.FlushPendingComposerDraftAsync();

        var persisted = JsonSerializer.Deserialize<Dictionary<string, string>>(js.Entries[DraftsStorageKey])!;
        persisted.ShouldNotContainKey($"new:{projectA}");
    }

    [Fact]
    public async Task FlushShouldBeNoOpWhenNothingPending()
    {
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var writesBefore = js.Entries.Count;
        await service.FlushPendingComposerDraftAsync();

        // Nothing changed in storage — the service didn't perform any setItem call.
        js.Entries.Count.ShouldBe(writesBefore);
    }

    [Fact]
    public async Task ShouldPersistAPendingDraftWhenTheServiceIsDisposed()
    {
        // Teardown inside the debounce window is the case the draft store exists to survive:
        // the user typed the last words of a message and the workspace went away before the
        // 500ms timer fired. Disposal used to cancel the timer and lose them.
        var (service, js) = CreateService();
        service.QueueComposerDraftSave("new:a", "half-written message");

        await service.DisposeAsync();

        js.Entries.ShouldContainKey(DraftsStorageKey);
        JsonSerializer.Deserialize<Dictionary<string, string>>(js.Entries[DraftsStorageKey])!
            .ShouldContainKeyAndValue("new:a", "half-written message");
    }

    [Fact]
    public async Task ShouldNotWriteOnDisposalWhenNothingIsPending()
    {
        var (service, js) = CreateService();

        await service.DisposeAsync();

        js.Calls.ShouldNotContain(call => call.Identifier == "localStorage.setItem");
    }

    [Fact]
    public async Task ShouldKeepSentMessagesNewestFirstPerProject()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await service.AppendComposerHistoryAsync(projectA, "first");
        await service.AppendComposerHistoryAsync(projectA, "second");
        await service.AppendComposerHistoryAsync(projectB, "elsewhere");

        // Up walks from the newest backwards, so index 0 has to be the last thing sent.
        service.GetComposerHistory(projectA).ShouldBe(["second", "first"]);
        // Scoped per project: another project's messages never surface here.
        service.GetComposerHistory(projectB).ShouldBe(["elsewhere"]);
    }

    [Fact]
    public async Task ShouldMoveARepeatedMessageToTheFrontInsteadOfDuplicatingIt()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        await service.AppendComposerHistoryAsync(project, "run the tests");
        await service.AppendComposerHistoryAsync(project, "fix the build");
        await service.AppendComposerHistoryAsync(project, "run the tests");

        // Otherwise the same phrasing — which is exactly what gets re-sent — would make the
        // user press Up past several copies of it to reach anything older.
        service.GetComposerHistory(project).ShouldBe(["run the tests", "fix the build"]);
    }

    [Fact]
    public async Task ShouldIgnoreBlankMessagesAndTrimTheRest()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        await service.AppendComposerHistoryAsync(project, "   ");
        await service.AppendComposerHistoryAsync(project, "  spaced  ");

        service.GetComposerHistory(project).ShouldBe(["spaced"]);
    }

    [Fact]
    public async Task ShouldCapHistoryAtTheNewestHundredEntries()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        for (var index = 0; index < 105; index++) await service.AppendComposerHistoryAsync(project, $"message {index}");

        var history = service.GetComposerHistory(project);
        history.Count.ShouldBe(100);
        history[0].ShouldBe("message 104");
        history[99].ShouldBe("message 5");
    }

    [Fact]
    public async Task ShouldPersistHistoryImmediatelyAndReloadIt()
    {
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        // No debounce here, unlike drafts: a send happens once, and losing the message that was
        // just sent to a tab close would defeat the point of storing it.
        await service.AppendComposerHistoryAsync(project, "do the recommended thing");

        js.Entries.ShouldContainKey(HistoryStorageKey);
        var reloaded = new WorkspaceStateService(js);
        await reloaded.InitializeAsync();
        reloaded.GetComposerHistory(project).ShouldBe(["do the recommended thing"]);
    }

    [Fact]
    public async Task ShouldReturnEmptyHistoryForAProjectThatNeverSentAnything()
    {
        var (service, _) = CreateService();
        await service.InitializeAsync();

        service.GetComposerHistory(Guid.NewGuid()).ShouldBeEmpty();
    }
}
