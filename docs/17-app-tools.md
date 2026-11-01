# Application management tools (App tools)

## Purpose

The built-in `App tools` MCP server gives the model CRUD over the application's own data: projects, chats, branches, messages, queues, global settings, directory grants, and tool policies. This enables scenarios like "create a chat from a chat", "set up a project for a repository", "explain why a tool refused", and history maintenance.

## Architecture

The server is built on the standard `ModelContextProtocol` library and speaks the regular MCP protocol: the same `tools/list` and `tools/call`, the same JSON schemas, the same result validation against the output schema. It differs from other servers only in its transport.

The transport is a pair of in-memory `System.IO.Pipelines` channels inside the Host process: `StreamServerTransport` on the server side and `StreamClientTransport` on the client side instead of the standard child-process streams. The client is a regular `McpClient`, so the session is handled by the same code as a stdio server.

The library itself generates tool schemas from method signatures: enums become string `enum` in the schema, parameters with default values become optional. There is no separate schema description.

The code is laid out as follows:

| Project | Contents |
|---|---|
| `AI.Tools/App` | five tools, `AppMcpServerHost`, shared mutation and pagination plumbing |
| `AI.Tools` | `McpToolSession` (shared by all servers), `DefaultToolSessionFactory`, `AppToolSessionFactory`, `CompositeToolSessionFactory` |

`IToolSessionFactory.OpenAsync` accepts a set of server IDs. `ChatAgent` computes it by the same rule for all servers — enabled globally, policy not `Deny`, not disabled in the project — so a disabled server is not started at all rather than started and filtered out.

The tools receive `IChatRunDispatcher` as `Func<IChatRunDispatcher>`: the dispatcher owns the agent, the agent owns the tool session, the session owns these tools. Lazy resolution breaks the construction cycle.

`App tools` is registered as a separate MCP server with a stable ID, the `InProcess` transport, appears in settings next to `Default tools`, and is **enabled by default**. `app_read`, `tool_search`, `skill_search`, `ask_user`, `app_navigate`, `app_theme`, `plan_update`, `subtask_wait` and `subtask_stop` default to `Allow`; tools that change data, execute skills, or delegate work default to `Ask`. Saved rules override these defaults. The global tool list offers `Use defaults` for already discovered tools.

`InProcess` is a full value of the domain enum `McpTransportKind`, not just a string in settings: saving a project-level policy binds the server to the project and records its transport. Both servers bundled with the Host appear in settings identically — name and transport are read-only; instead of a command line, the list of tool policies is shown, and they cannot be removed.

## Tool composition

The tools are grouped by risk level: the tool boundary matches the boundary of what the user allows with a single `Allow` button. The model sees them with the `mcp_app__` prefix.

| Tool | Operations |
|---|---|
| `app_read` | read any application resource and search messages |
| `app_chats` | `Create`, `Rename`, `Pin`, `SetEndpoint`, `RenameBranch`, `Archive`, `Restore`, `ArchiveBatch`, `UndoArchive`, `Delete`, `DeleteBranch`; archiving skips scheduled chats unless `removeSchedules=true` removes all branch schedules first |
| `app_runs` | `Submit`, `Stop`, `UpdateQueued`, `RemoveQueued`, `ClearQueue`, `Resume`, `SkipFailed`, `Rebase`, `MarkRead` |
| `app_projects` | `Create`, `Update`, `Delete` |
| `app_security` | policies and grants for the project, chat, and globally; global settings; write credentials |
| `app_navigate` | opens a project, chat or branch in the user's window; changes no data |
| `app_theme` | `Get`, `Preview`, `Save`, `Activate`, `Discard` for the window's colour themes — see [app_theme](#app_theme) |
| `app_resources` | create and retire project file/directory references; create and update named chat reviews; attach them with `app_runs Submit` |
| `app_memory` | `Create`, `Update`, `Delete` long-term memory entries — see [25-memory-and-instructions.md](25-memory-and-instructions.md) |
| `app_instructions` | replace the current project's instructions; kept apart so it still asks where other writes are allowed |
| `app_schedule` | `Get`, `Set`, `Pause`, `Resume`, `Remove`, `RunNow`, `ReportRun` for a chat's schedule — see [35-scheduled-chats.md](35-scheduled-chats.md) |
| `spawn_subtask` | start one independent task and immediately return its run-owned handle |
| `subtask_wait` | collect statuses and terminal results of owned handles, waiting for any or all |
| `subtask_stop` | cancel selected owned tasks and collect their terminal results |
| `ask_user` | ask the user a question and wait for an answer — see [19-ask-user.md](19-ask-user.md) |
| `plan_update` | lay out and move the plan the user watches during a multi-step task, with its optional criteria of done; changes no data — see [37-chat-plans.md](37-chat-plans.md) |

### app_read

Resources: `Projects`, `Project`, `Chats`, `Chat`, `Messages`, `Runs`, `Settings`, `Search`, `Resources`, `Reviews`, `Review`, `Memory`, `Instructions`. The response always has one shape — a page of items — so a single document is a page of one item.

`Resources` lists the project's reusable file and directory references, including their revision and retired state. A reference contains a path and kind, never file contents. `app_resources Create` checks the project's read grants, canonicalizes the path and returns the reference; `Retire` uses an expected revision. To attach references to a turn, pass them in `app_runs Submit`. The queued and stored user message keep the same structured refs, and the model receives a short manifest rather than file bodies.

`Reviews` lists the named review resources of one chat, across its branches; `Review` reads one resource with bounded comments. `app_resources CreateReview` selects files from one saved assistant change set. `UpdateReview` changes its name, selected files, and comments using the current revision as a concurrency check. Review resources are mutable; a later model request resolves their current comments.

`Chat` is returned without messages: they are a separate resource, otherwise reading a heading would drag the entire history in. `Messages` with `branchId` returns the chain from the branch head to the root — exactly the context the model sees. Tool results come without their body (`contentOmitted: true`), as in the transcript: a single file read, or an earlier read of the same branch, is easily larger than the rest of the page, and each read would otherwise carry the previous one inside it. `resourceId` set to a message id returns that one message whole.

`Runs`, and the `current` run that `app_runs` returns, are compact: the open page's message tail, draft, diffs and turn usage are left out, and queued messages, the arguments of tools in flight and the streaming answer are clipped. A run submitting a long message would otherwise return that message again in its own result.

`McpTools` lists the declared tools of one enabled server as a compact policy inventory by default:
names, schema hashes, default decisions, annotations, descriptions cut to 240 characters and the
top-level parameter names. `query` with an exact original tool name returns that tool's full input
schema; `includeSchemas=true` returns full schemas for a whole page. Full schemas were the default
until 2026-10: one server's page ran to over a hundred thousand characters, which stayed in the
context for the rest of the run, and a request that large outlasted the endpoint's first-token
timeout on every retry.

A page is bounded simultaneously by the number of items (`limit`, default 100, max 1000) and by a budget of 262144 characters — the same as for the file tools. The cursor is the decimal offset of the next item, opaque by contract: the caller returns what it received and never computes it on its own. The first item of the page is always included, otherwise a document larger than the budget would become unreachable.

### Message search

The `Search` resource searches text across messages of all chats at once. Previously this capability did not exist in the application at all — not in tools, not in the API, not in the UI; the only way was iterating chats one by one, which hit the character budget long before the end.

`projectId`, `chatId`, and `branchId` are optional and narrow the scope; without them, the whole application is searched. Matching works like in `grep_files`, to avoid a second dialect: literal substring by default, `ignoreCase` enabled, `isRegex` switches to a regular expression in `NonBacktracking` mode — runtime is linear in string length, so a pattern the model comes up with does not hang the Host, but backreferences and lookaround are rejected with a clear error.

`roles` defaults to `User` and `Assistant`: messages with the `Tool` role are serialized call results, often tens of kilobytes of JSON, and would drown the output.

The result is one element per matched message: project and chat IDs and names, message ID, role, time, a fragment around the first match, and `matchCount` for the whole message. The full message is not returned.

There is no index: chats are read in immutable order (by project ID, then chat ID) and scanned. This is honest about cost and there is nothing to rebuild; the explicit limits keep it usable — match count, character budget, and a cap on scanned messages. When any of them is reached, the search reports `truncated` and returns a cursor. The scan order intentionally does not match the sidebar order: that one sorts by activity, and under paged search the cursor would point to a different chat every time.

The same search is available as `GET /api/chats/search` and as the search string in the sidebar: while the query is active, the results replace the project tree, and selecting a result opens the right chat.

### app_runs

The `wait` flag:

- `false` — returns control immediately after accepting the message;
- `true` — watches the run until it stops on its own, requires a person, or `waitTimeoutMs` (1 s to 10 min) expires. Only the states the run reached after the send count: a snapshot with a status from the previous run is recognized by its revision and skipped, otherwise a chat that answered a minute ago would immediately report a fictional `Completed`.

The timeout expiring is not an error: the message is accepted either way, so the last known status is returned. The wait is also bounded by the tool's own policy, so a lingering call is killed from the outside.

`branchId` defaults to `chatId`: the original branch ID matches the chat ID.

Every message `app_runs Submit` writes carries its sender, the chat and branch of the calling run, set by the server; the optional `intent` (`Question`, `Answer`, `Decision`, `Status`, `Blocker`, `Done`) is stored with it. `mode: Aside` starts no turn and ignores `wait`: a running turn reads the message at its next step, an idle branch just records it. See [Asides and team messages](34-asides-and-team-messages.md).

`mode: Fork` takes an optional `title` for the new branch, so it needs no separate rename. `app_chats RenameBranch` (and the HTTP rename) no longer compares the chat revision: a title conflicts with nothing written meanwhile, and while branches run the revision moves with every message, so a rename against the revision the caller had read kept failing for as long as a team worked.

### Subtask lifecycle

`spawn_subtask(task, acceptanceCriteria?, connectionId?)` starts one bounded assignment and
returns `{taskId, task, status}` immediately. Status is `queued` or `running`; completion later
becomes `completed`, `failed` or `cancelled`. Put essential context, the absolute workspace,
owned paths and restrictions in `task`. The Host binds the current project, chat and branch;
the model does not supply those ids. Several scalar launches overlap even when their MCP calls
run sequentially. There is no batch-launch API or `sharedInstructions` parameter.

Keep each `taskId`, do independent work, then call
`subtask_wait(taskIds, mode="any", waitTimeoutMs=60000)`. `any` returns as soon as at least one
selected task is terminal; `all` waits for every selected task. A zero timeout takes an immediate
snapshot. The bounded wait returns current states at timeout, with `result` on terminal entries and
`timedOut=true` when a positive wait elapsed before its condition was satisfied. Repeated
bounded waits on active work do not trigger the Host's no-progress stall detector.
After consuming a result, wait on remaining handles. Waiting again replays results and never
starts another task. Failure of one task does not cancel its siblings.

`subtask_stop(taskIds)` validates the entire selection belongs to the calling run, cancels it,
waits for cancellation to settle and returns terminal results. Finished tasks keep their result.
A cancelled wait, policy timeout or new joined user message ends only that wait: children remain
alive. Stop or disposal of the parent run cancels and drains all its children. Handles expire at
the end of that run and cannot be used by another branch, later run or after a Host restart.
The Host prevents a normal final answer while owned results remain uncollected, using the normal
permission-checked wait path. A stopped or failed result must be inspected rather than treated
as successful work. The same lifecycle applies when the parent is a teammate branch: collect
workers' results before sending the lead one integrated report.

**Results and evidence.** Each terminal result contains the answer, selected connection,
artifacts, tool-call count, elapsed time, checks actually performed and unresolved items. The
worker returns a machine-readable evidence envelope that the Host separates from its answer.
Trust passed local checks unless evidence conflicts; check the combined result rather than
repeating every worker check. Stopped work may have partial artifacts and unresolved items.
Each worker has its own workspace baseline. No separate chat is persisted.

The full conversation is attached to wait/stop results only as `_meta.transcript`; it remains
visible in the existing expandable tool card and survives in the parent chat's tool history.
`ModelContent` excludes that transcript. This keeps delegated detail inspectable without
importing it into the caller's model context. Wait progress reports each selected task's
current activity (thinking, writing or tool name) and the completed count.

**Models.** An explicit enabled `connectionId` wins; otherwise scalar launches rotate through
enabled connections marked `ForSubtasks`, then fall back to the caller's branch connection.
Explicit selections do not advance the pool. User connection ratings, `goodFor` and prices
remain available through `app_read` Settings; they are not embedded in the tool schema.

**Permissions.** Workers inherit project grants and the caller's effective approval mode.
They cannot request interactive approval: an unresolved `Ask` is refused. Establish a narrow
permission in the caller only when authorized, or choose an already allowed equivalent. Do not
loosen permissions merely for parallelism. Generic execution and basic file tools remain core
in adaptive selection for parents and workers.

**Resource limits.** There is no model-facing batch-size restriction. Internally the Host retains
at most 256 handles per parent run and executes at most 32 workers concurrently. Excess root
work queues. Nested work never waits behind occupied parents when all slots are full: it fails
with a bounded error, so the caller can continue directly. Each worker has a 40-model-step budget
and nesting still respects `MaxSubtaskDepth` (default 1). These guards apply to team members too;
being a teammate does not itself consume a subtask depth level.

For large transformations, measure size and natural section boundaries before reading delegated
content, verify ranges cover the source without gaps or overlaps, launch each range separately
with disjoint output paths, collect all handles, and join the outputs in one execution call.
Do not duplicate the source in the briefing or ask workers to reproduce their inputs.

## Scope

The tools work over **the entire application**: the agent reads and changes any projects, chats, and global settings, not just the current chat's project.

## Boundaries

### Secrets

DPAPI-protected endpoint profile API keys are **writable but not readable**. `app_read` returns only the `HasCredential` flag. When global settings are replaced, the secret presence flags are recalculated from the protected store, not taken from the supplied document.

### Privilege escalation

There are **no special restrictions**: the agent can do everything the user can do through the UI, including extending its project's directory grants, changing `app_*` tool policies, and enabling MCP servers. A deliberate decision for a local single-user application; recorded in [ADR-007](decisions/ADR-007-in-process-app-tools.md).

### Recursion

There are **no restrictions** on chat spawning depth, child run budget, or writes to the parent chat. The agent loop's limits and the stop button remain the controls.

`spawn_subtask` is the exception: delegated work nests at most `MaxSubtaskDepth` levels (chat automation settings, default 1). At the default a chat may delegate and its subtasks may not. The tool stays in a subtask's list — the list is part of the prompt prefix a subtask may share with its parent — and the call is refused with a message telling the subtask to do the work itself or report it as unresolved; the subtask's `run.subtask` instruction says the same up front. A value below 1 is read as 1: forbidding delegation altogether is what a `Deny` policy on the tool is for.

## Mutation behavior

- **One operation per call.** No batched mutations.
- **`dryRun` by default** for destructive operations — chat, branch, and project deletion. Other operations ignore the flag. The planned call describes the effect and writes nothing.
- **Revisions are required.** On a mismatch, an `isError` is returned with the `Conflict` status, the current revision, and the current document; the model decides whether to retry. There is no automatic retry. Chat mutations do not distinguish "no chat" from "someone wrote earlier", so the chat is re-read to tell them apart.
- **Idempotency by `operationId`.** A repeat call with the same identifier returns the previous result with the `replayed` flag and writes nothing. Only applied mutations are remembered: a dry run and a rejected call did not change anything. The journal lives within the Host process — it closes the loop inside a run and deliberately does not survive a restart.
- `app_runs Submit` passes `operationId` to the dispatcher as the message ID, so a repeat does not create a second message with a different identity. In `app_runs` the id is optional: models cannot draw a random UUID and write patterns that collide, so when it is omitted the application assigns one and returns it as `messageId` (for a Fork also the new branch's id). An id reused for a different message is refused instead of being answered with the earlier result.

Snapshots with rollback, diff in the confirmation card, and an audit with before/after hash are not in scope.

`dryRun` is understood only by destructive operations, and for those it is on by default. Other operations reject an explicit `dryRun: true` rather than ignoring it: silently applying a change the caller thought was a rehearsal is the worst possible outcome.

## The agent grants itself directory access

There is no prohibition: `SetProjectSecurity` accepts any root, and the domain only checks that the name is non-empty, the path is non-empty, and the tool list is non-empty. No list of forbidden paths, no nesting requirement. Between the agent and any directory on the machine stands one confirmation card — by the user's decision, it remains the only limit.

The real check lives elsewhere and works independently: `PathGuard` inside the built-in server's process refuses by default, requires a fully qualified path, canonicalizes it, resolves reparse points along the entire chain of existing components, and only then checks containment in the grant with the required capability. An empty grant set means refusal, not full access.

**The grant takes effect before the next model step in the same run.** The Host re-reads project grants between model steps. When they change, it reopens the tool session so the built-in server receives the new grants at startup. Tool calls already submitted in the same batch finish with the previous session. A tool omitted from the model request because of the schema budget is a separate tool-discovery condition, not a directory-access failure.

For one directory, use `AddDirectoryGrant` with the project revision and a single grant. It leaves MCP server bindings and tool policies intact. `RemoveDirectoryGrant` revokes one grant by `grantId` the same way. `SetProjectSecurity` still replaces the full security document and should be reserved for intentional full replacements.

For several directories, use `AddDirectoryGrants` with `projectId`, one `revision` and a
`directoryGrants` array of the same grant payloads. The batch is validated before mutation and
saved once: an invalid grant or conflicting ID adds nothing, and a stale revision returns a
conflict. Equivalent canonical-root, recursive and capability grants are skipped, including
duplicates within the batch; display names and existing grants are preserved. IDs cannot be reused
for different access. Path comparisons use the supplied canonical spelling. The additions appear
as ordinary folders in the existing project settings and can be revoked individually.

The `code-cli-configure` playbook diagnoses inaccessible CLI installation, configuration and cache
directories, prefers the user's existing paths, obtains any missing authorization with the usual
directory picker, and applies the approved list through `AddDirectoryGrants`. It verifies the
original command in the next model step, after tool sessions refresh their grants. Both the process
tool description and standing instructions point to this skill; the skill body loads only when
used. No separate CLI permission store or environment-management tool is introduced.

## Confirmations and call batches

While a confirmation card is up, the Host re-reads the effective policy every two seconds. Permission granted in settings or through the `app_security` tool releases the waiting call in the same way as the button on the card, and prohibition rejects it. Without this, a user who went to change settings instead of clicking the button would only wait for the timeout.

Confirmations are requested one at a time: permission for one call must not extend to its neighbor. The card reports which call this is out of how many in the batch.

If a batch is aborted — by stop or by transport failure — the response is recorded not only for the refusing call, but for every call after it. The model's message with `tool_calls` is considered valid only when every one of them has a response; otherwise the history becomes one that the endpoint rejects, and the run cannot be continued or repeated. The `Paused` state is published before the agent finishes writing these responses, so a client that read the history immediately after the stop may catch it halfway — the run's continuation goes through the same lock and will not see such history.

`app_runs` commands add to their `effect` that the run is waiting on human confirmation. Previously, `Resume` reported success while being outwardly indistinguishable from a real resumption.

## UI notification of changes

The `GET /api/runs/events` stream carries two kinds of frames. In addition to the previous `snapshot`, a **shared `data-changed` signal with no payload** has been added: it does not say what exactly changed.

It is published after any `app_*` mutation is committed. A subscriber receives at most one wake-up per series of changes: each listener has a one-element channel with eviction of the old value, and the server additionally waits 250 ms before sending the frame. The subscription is registered at the moment of the call, not at the moment the enumeration begins — otherwise a change in that interval would be lost.

On receiving the signal, the Web UI re-reads what it shows: the project list, the selected project's chats, and the open chat. The selection is preserved if the selected object still exists. Both frames go through the same channel, so only one loop writes to the response body.

Changes made through the UI's own HTTP API do not publish a signal: the client that made them already knows.

### app_theme

Themes are kept by the client (`ai-client.settings`), not by the Host, so `app_theme` reaches them through the window. It sends an `AppNavigation` whose `Theme` is an `AppThemeRequest` on the same channel as `app_navigate`; one window claims it — the one showing the asking chat or holding focus answers first, others wait 400 ms — and completes it with an `AppThemeState`: the effect, the preset and accent, the saved themes with their seeds and the unsaved preview with its main colours and every measured contrast pair. A background run gets `unavailable` without asking a window.

The window's side is `IThemeChatRequests` in `AI.Web/Settings`, built on `IThemeService` like the studio. A theme is four seeds (background, family, contrast, saturation) and the window derives the palette with `ThemePalette`, so a model can only ask for a theme the studio could produce. `Preview` paints without writing anything and continues the previous preview, so each step names only the seeds that change; `Save` stores it in place (`themeId`) or as a new theme and switches to it; `Activate` switches to a saved theme or a preset; `Save` and `Activate` take an accent; `Discard` repaints the saved theme. A preview never outlives the page. The tool defaults to `Allow` because a preview only paints the window; the playbooks `settings-create-theme` and `settings-improve-theme` save or switch only after the user agreed through `ask_user`. Deleting a theme is left to Settings.

A third frame, `event: navigate`, carries an `AppNavigation` (`ProjectId`, optional `ChatId` and `BranchId`). `app_navigate` publishes it through `IAppNavigationSignal` after checking that the project, chat and branch exist; a background run (`Interactive` false) gets an error instead, so a window nobody is watching is not moved. Each listener keeps only the latest request. The Web re-reads the project list, and the chats when the project is already open, because the target may be newer than the pending `data-changed` frame; then it selects the target as a link would and pushes a history entry, so Back returns to where the user was. `project-create`, `chat-compact` and `chat-fork` call it for the project, chat or branch they create.

## Presentation in the UI

For `app_*` calls, dedicated `IToolPresentationAdapter` implementations exist. Reads are shown as "Read messages" with the page size and cursor; a change is the phrase returned by the tool itself ("Created chat 'Notes'"). A revision conflict and a dry run are shown as a warning, not an error: the data was not harmed, but the user should notice it. The adapter is matched by server prefix, not just tool name: names are unique only within one server.

## Agent loop limits

The default per-tool call limit is 56535, and the default timeout is 600 seconds with a configurable maximum of 86400 seconds. Explicit chat, project and global limits still override these defaults.

- `ToolPolicy.MaxCallsPerRun` defaults to `56535`.
- The cap on parallel `tool_calls` in a single assistant message has been raised from `20` to `1024` (`ChatCompletionSseParser`). This is protection against a malformed stream, not a run limit.
- There is no separate model iteration limit and none is being introduced: any finite limit would be a tightening.

## Verification

- `AppDataChangeSignalTests` and `AppToolPresentationTests` unit tests;
- `AppToolTests` — discovery and calls through a real MCP session over the in-process transport, against real application services: cursor-based pagination, absence of secrets, operation replay, dry run, revision conflict, security settings replacement, rejection of out-of-schema arguments;
- smoke test through the real Host: `GET /api/mcp/default/tools` returns 21 tools (16 built-in and 5 application), and a chat under the model's control created another chat through `app_chats` and put a message in it through `app_runs`; the chat list in the open UI updated on the `data-changed` signal without reloading the page.

## Current ids and forgiving arguments

A run with the App server gets one system line naming its `projectId`, `chatId` and `branchId`,
because several tools take them as ids and nothing else in the context says which ones the run
belongs to. Where a tool knows the run, an omitted id means the current one: project-scoped
`app_read` resources, `Chat`, `Messages` and `Reviews` in `app_read` (current chat), and
`spawn_subtask`, `subtask_wait` and `subtask_stop` (current parent run).

Before the input schema is checked, the Host repairs three common slips in top-level arguments: an
object or array sent as its JSON text is parsed when the schema does not also accept a string, an
enum value in the wrong case is replaced by the one value it matches, and a number past its
inclusive `minimum` or `maximum` is brought back to that bound. Those bounds cap how much a call
returns or waits, so the bound is what the tool would have applied anyway; models kept asking for
200 matches or 15 context lines, and each refusal cost a whole step. Anything else still fails
with the property and reason in the error. Labels quoted in tool descriptions, such as
"(Recommended)", are examples: questions and options are written in the user's language.
