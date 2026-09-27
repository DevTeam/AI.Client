namespace AI.Web.Tests.State;

using System.Text.Json;
using AI.Web.State;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

public class WorkspaceStateServiceTests
{
    private const string DraftsStorageKey = "ai-client.composer-drafts.v1";
    private const string HistoryStorageKey = "ai-client.composer-history.v1";
    private const string ProjectContextStorageKey = "ai-client.project-context.v1";

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

    [Fact]
    public async Task ShouldReturnNullForProjectWithoutStoredContext()
    {
        // A fresh project has no remembered chat/branch — the sidebar must fall back to its
        // "new chat" composer draft instead of trying to restore an empty tuple.
        var (service, _) = CreateService();
        await service.InitializeAsync();

        service.GetProjectContext(Guid.NewGuid()).ShouldBe((null, null));
    }

    [Fact]
    public async Task ShouldRoundTripProjectContextThroughLocalStorage()
    {
        // Switching from project A → B → A should reopen A on the same chat/branch the user
        // was on when they left it. The persistence path is what carries the remembered
        // values across that round trip.
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        var chatA = Guid.NewGuid();
        var leafA = Guid.NewGuid();
        await service.SetProjectContextAsync(projectA, chatA, leafA);

        var reloaded = new WorkspaceStateService(js);
        await reloaded.InitializeAsync();
        reloaded.GetProjectContext(projectA).ShouldBe((chatA, leafA));
    }

    [Fact]
    public async Task ShouldOverwriteStoredContextWhenTheSameProjectIsReopened()
    {
        // The user opened chat X with branch Y, then opened chat P with branch Q in the same
        // project. The second selection must replace the first — not stack behind it — so
        // returning to the project lands the user on the chat they actually left.
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        var chatX = Guid.NewGuid();
        var leafY = Guid.NewGuid();
        var chatP = Guid.NewGuid();
        var leafQ = Guid.NewGuid();

        await service.SetProjectContextAsync(project, chatX, leafY);
        await service.SetProjectContextAsync(project, chatP, leafQ);

        var context = service.GetProjectContext(project);
        context.ShouldBe((chatP, leafQ));
    }

    [Fact]
    public async Task ShouldKeepProjectContextsIndependent()
    {
        // One project's remembered chat/branch must never surface under another project's
        // key — otherwise switching projects briefly would scramble the other one's restore.
        var (service, _) = CreateService();
        await service.InitializeAsync();

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var chatA = Guid.NewGuid();
        var leafA = Guid.NewGuid();
        var chatB = Guid.NewGuid();
        var leafB = Guid.NewGuid();

        await service.SetProjectContextAsync(projectA, chatA, leafA);
        await service.SetProjectContextAsync(projectB, chatB, leafB);

        service.GetProjectContext(projectA).ShouldBe((chatA, leafA));
        service.GetProjectContext(projectB).ShouldBe((chatB, leafB));
    }

    [Fact]
    public async Task ShouldPersistProjectContextImmediatelyWithoutWaitingForADebounce()
    {
        // The post-render settle code in Home.razor relies on the context being on disk by the
        // time it returns — a debounced write would lose the last selection to a tab close.
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        var chat = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        await service.SetProjectContextAsync(project, chat, leaf);

        js.Entries.ShouldContainKey(ProjectContextStorageKey);
        var persisted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(js.Entries[ProjectContextStorageKey])!;
        persisted.ShouldContainKey(project.ToString());
        var entry = persisted[project.ToString()];
        entry.GetProperty("ChatId").GetGuid().ShouldBe(chat);
        entry.GetProperty("BranchLeafId").GetGuid().ShouldBe(leaf);
    }

    [Fact]
    public async Task ShouldPreserveBranchLeafEvenWhenItLiesPastTheMainFork()
    {
        // Regression for the project-switch restore path in SelectChatAsync: it remembers the
        // last (ChatId, BranchLeafId) the user left on and re-applies it when they come back to
        // the same project. The leaf of a non-main branch is, by construction, a message that
        // lives past the fork point and is therefore NOT present in the main transcript. The
        // workspace state service must round-trip that leaf unchanged; otherwise the restore
        // step loses it before it ever reaches the branch-match check.
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        var chat = Guid.NewGuid();
        var mainLeaf = Guid.NewGuid();
        var branchLeaf = Guid.NewGuid();

        // First selection lands on main: that's the cheap default and what
        // GetOriginalBranchLeaf returns on the restore path.
        await service.SetProjectContextAsync(project, chat, mainLeaf);
        // Then the user walks into a branch. The leaf is on the branch, not on main.
        await service.SetProjectContextAsync(project, chat, branchLeaf);

        var reloaded = new WorkspaceStateService(js);
        await reloaded.InitializeAsync();
        reloaded.GetProjectContext(project).ShouldBe((chat, branchLeaf));
    }

    [Fact]
    public async Task ShouldNotWriteProjectContextWhenNothingHasChanged()
    {
        // The post-render settle code calls SetProjectContextAsync on every chat open. A no-op
        // detection keeps that path from spamming localStorage — and avoids thrashing the
        // last-project-modified timestamp, which other UI elements watch.
        var (service, js) = CreateService();
        await service.InitializeAsync();

        var project = Guid.NewGuid();
        var chat = Guid.NewGuid();
        var leaf = Guid.NewGuid();

        await service.SetProjectContextAsync(project, chat, leaf);
        var writesAfterFirst = js.Calls.Count(call => call.Identifier == "localStorage.setItem");

        await service.SetProjectContextAsync(project, chat, leaf);
        js.Calls.Count(call => call.Identifier == "localStorage.setItem").ShouldBe(writesAfterFirst);
    }
}
