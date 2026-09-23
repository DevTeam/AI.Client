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
| `AI.Client.Server/Infrastructure/Tools/App` | five tools, `AppMcpServerHost`, shared mutation and pagination plumbing |
| `AI.Client.Server/Infrastructure/Tools` | `McpToolSession` (shared by all servers), `DefaultToolSessionFactory`, `AppToolSessionFactory`, `CompositeToolSessionFactory` |

`IToolSessionFactory.OpenAsync` accepts a set of server IDs. `ChatAgent` computes it by the same rule for all servers — enabled globally, policy not `Deny`, not disabled in the project — so a disabled server is not started at all rather than started and filtered out.

The tools receive `IChatRunDispatcher` as `Func<IChatRunDispatcher>`: the dispatcher owns the agent, the agent owns the tool session, the session owns these tools. Lazy resolution breaks the construction cycle.

`App tools` is registered as a separate MCP server with a stable ID, the `InProcess` transport, appears in settings next to `Default tools`, and is **enabled by default**. All tools receive the `Ask` policy on first discovery.

`InProcess` is a full value of the domain enum `McpTransportKind`, not just a string in settings: saving a project-level policy binds the server to the project and records its transport. Both servers bundled with the Host appear in settings identically — name and transport are read-only; instead of a command line, the list of tool policies is shown, and they cannot be removed.

## Tool composition

Seven tools. Five are grouped by risk level: the tool boundary matches the boundary of what the user allows with a single `Allow` button. The model sees them with the `mcp_app__` prefix.

| Tool | Operations |
|---|---|
| `app_read` | read any application resource and search messages |
| `app_chats` | `Create`, `Rename`, `Pin`, `SetEndpoint`, `RenameBranch`, `Delete`, `DeleteBranch` |
| `app_runs` | `Submit`, `Stop`, `UpdateQueued`, `RemoveQueued`, `ClearQueue`, `Resume`, `SkipFailed`, `Rebase`, `MarkRead` |
| `app_projects` | `Create`, `Update`, `Delete` |
| `app_security` | policies and grants for the project, chat, and globally; global settings; write credentials |
| `spawn_subtask` | run a task in a separate conversation and return only its result |
| `ask_user` | ask the user a question and wait for an answer — see [19-ask-user.md](19-ask-user.md) |

### app_read

Resources: `Projects`, `Project`, `Chats`, `Chat`, `Messages`, `Runs`, `Settings`, `Search`. The response always has one shape — a page of items — so a single document is a page of one item.

`Chat` is returned without messages: they are a separate resource, otherwise reading a heading would drag the entire history in. `Messages` with `branchId` returns the chain from the branch head to the root — exactly the context the model sees.

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

### spawn_subtask

Delegates work to a separate conversation and returns only the answer. The point is the audience split already built into `ToolCallResult`: `ModelContent` is the only thing that enters the model context, and `_meta` is excluded from that projection by contract. Therefore the subtask's result goes to the calling model, while its entire conversation — reasoning, every tool call, every result — is placed into `_meta` and is visible only in the UI, as a folded block inside the call card.

This is fundamentally cheaper than having a second chat do the work and then reading it through `app_read`: reading would pull into context exactly the text whose absence the work was delegated for.

Nothing is saved. The subtask's messages live in the list for the duration of the call; there is no chat that can be opened afterwards — the price of also having nothing to clean up afterwards. The call record with the full conversation stays in the parent chat's history as a regular tool message.

`projectId` and `chatId` are passed explicitly: ambient context does not cross the MCP boundary — the server handles the call in its own message loop — so the calling model names the project and chat it is working in. The subtask inherits directory grants and tool policies from them, and by default the connection too. An arbitrary endpoint cannot be set: only what the user has configured can be chosen.

**Confirmations are not available to the subtask.** Nobody is watching the background run, so any tool whose policy is `Ask` is refused, and the subtask reports what it could not do. The necessary tools should be allowed in advance.

**Model.** Each task is an object with text and an optional `connectionId`. The connection is selected by chain: the task's own, otherwise the call's common one, otherwise one of those marked in settings as connections for subtasks, otherwise the calling chat's connection. The named connection must exist and be enabled; an arbitrary endpoint cannot be set — it is chosen from what the user has configured. The used connection is returned in the result and visible in the card.

Connections for all tasks are resolved before work begins: a batch that would fail on the last task's endpoint anyway should not first spend a minute on the rest.

**"For subtasks" flag and connection ratings.** In settings, a connection can be marked as the one delegated work goes to when nobody addressed it explicitly — usually a cheap fast model. Unlike the "default" flag, this one can be set on multiple connections at once: tasks that did not name a connection are distributed among marked connections in round-robin, so a fan of subtasks is answered by several providers rather than queued to one. The queue is counted only by unaddressed tasks, so one pinned task does not shift the rest. The flag is removed together with `Enabled`: a disabled connection receives no subtasks.

The flag forbids nothing. An explicit `connectionId` works for any enabled connection, otherwise the scenario this argument was introduced for — checking all connections at once — would become impossible.

Next to the flag, the connection carries two coarse ratings from 1 to 5 — `capability` and `cost` — and a `goodFor` string about what the ratings do not express: long context, vision, locality. This is the user's opinion, not a fact about the model, so the scale is deliberately short, and an unrated connection returns `null` rather than the middle: "nobody rated it" must be distinguishable by the model from "average". All of this arrives through `app_read` with the `Settings` resource — no separate mechanism is needed — and the model chooses what to pay for a task.

The ratings are not placed in the description of `spawn_subtask` itself. The description is part of the tool schema, and the hash is computed from the schema, to which permissions are tied — moving a slider would reset the tool's permission to `Ask`.

A task with its own connection exists for parallelism. Tools in a single model response run sequentially, so "one task — one call" queues the connection checks: fourteen connections at one timeout each are fourteen waits in a row. Tasks in one call, however, run in parallel, so the same fourteen are checked by two calls.

**Live progress.** The subtask reports what it is doing via MCP `notifications/progress`: "thinking", "writing", or the name of the tool it is currently calling. The message reaches the active call row in the parent card through the same path as progress for any other server. With multiple tasks, the row numbers them and shows how many have already finished. Otherwise a subtask that goes silent is indistinguishable from a stuck one.

**Limits.** Up to 8 tasks per call; they run in parallel; no more than 8 subtask runs simultaneously across the entire Host. A nested subtask keeps its parent open, so the same counter limits both width and depth: runaway recursion hits it, not machine memory. One task failing does not cancel its neighbors.

The tool's policy timeout measures the call's silence, not its duration. Otherwise a single number would have to suit two addressees at once — reading a file and a fan of subtasks — and the fan would lose: it would die halfway through with all work already started, even though its progress notifications were saying it was working. MCP allows resetting the timeout on a progress notification for exactly this reason. On top sits a hard ceiling of 30 minutes per call, so a looping tool that faithfully reports on itself does not run forever.

The subtask has its own branch for tracking changes in the working directory, so its file edits are attributed to it, not to the parent's summary; the number of changed files is returned in the result.

What is not there: a subtask does not survive a restart; it cannot be resumed or extended. It is a single bounded call, not a place to return to.

## Scope

The tools work over **the entire application**: the agent reads and changes any projects, chats, and global settings, not just the current chat's project.

## Boundaries

### Secrets

DPAPI-protected endpoint profile API keys are **writable but not readable**. `app_read` returns only the `HasCredential` flag. When global settings are replaced, the secret presence flags are recalculated from the protected store, not taken from the supplied document.

### Privilege escalation

There are **no special restrictions**: the agent can do everything the user can do through the UI, including extending its project's directory grants, changing `app_*` tool policies, and enabling MCP servers. A deliberate decision for a local single-user application; recorded in [ADR-007](decisions/ADR-007-in-process-app-tools.md).

### Recursion

There are **no restrictions** on chat spawning depth, child run budget, or writes to the parent chat. The agent loop's limits and the stop button remain the controls.

## Mutation behavior

- **One operation per call.** No batched mutations.
- **`dryRun` by default** for destructive operations — chat, branch, and project deletion. Other operations ignore the flag. The planned call describes the effect and writes nothing.
- **Revisions are required.** On a mismatch, an `isError` is returned with the `Conflict` status, the current revision, and the current document; the model decides whether to retry. There is no automatic retry. Chat mutations do not distinguish "no chat" from "someone wrote earlier", so the chat is re-read to tell them apart.
- **Idempotency by `operationId`.** A repeat call with the same identifier returns the previous result with the `replayed` flag and writes nothing. Only applied mutations are remembered: a dry run and a rejected call did not change anything. The journal lives within the Host process — it closes the loop inside a run and deliberately does not survive a restart.
- `app_runs Submit` passes `operationId` to the dispatcher as the message ID, so a repeat does not create a second message with a different identity.

Snapshots with rollback, diff in the confirmation card, and an audit with before/after hash are not in scope.

`dryRun` is understood only by destructive operations, and for those it is on by default. Other operations reject an explicit `dryRun: true` rather than ignoring it: silently applying a change the caller thought was a rehearsal is the worst possible outcome.

## The agent grants itself directory access

There is no prohibition: `SetProjectSecurity` accepts any root, and the domain only checks that the name is non-empty, the path is non-empty, and the tool list is non-empty. No list of forbidden paths, no nesting requirement. Between the agent and any directory on the machine stands one confirmation card — by the user's decision, it remains the only limit.

The real check lives elsewhere and works independently: `PathGuard` inside the built-in server's process refuses by default, requires a fully qualified path, canonicalizes it, resolves reparse points along the entire chain of existing components, and only then checks containment in the grant with the required capability. An empty grant set means refusal, not full access.

**The grant takes effect from the next run.** The tool session is opened once per run, and the grants are sent to the built-in server as an environment variable when its process starts — the process is already running, the variable has already been read. Therefore an agent that has granted itself access cannot use it in the same run.

This is stated in the `app_security` description, because otherwise the model reads a `PathGuard` refusal as a wall and asks the user to go to settings — advice that is practically correct, but describes the wall where there is a door with a delay. It needs to know about both the door and the delay: without the second, it grants access and immediately tries to use it, gets the same refusal, and decides the grant did not work.

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

## Presentation in the UI

For `app_*` calls, dedicated `IToolPresentationAdapter` implementations exist. Reads are shown as "Read messages" with the page size and cursor; a change is the phrase returned by the tool itself ("Created chat 'Notes'"). A revision conflict and a dry run are shown as a warning, not an error: the data was not harmed, but the user should notice it. The adapter is matched by server prefix, not just tool name: names are unique only within one server.

## Agent loop limits

Limits are not tightened: a value changes only if the new one is larger than the current one.

- `ToolPolicy.MaxCallsPerRun` defaults to `65535` — **left as is**; 1024 would be a tightening.
- The cap on parallel `tool_calls` in a single assistant message has been raised from `20` to `1024` (`ChatCompletionSseParser`). This is protection against a malformed stream, not a run limit.
- There is no separate model iteration limit and none is being introduced: any finite limit would be a tightening.

## Verification

- `AppDataChangeSignalTests` and `AppToolPresentationTests` unit tests;
- `AppToolTests` — discovery and calls through a real MCP session over the in-process transport, against real application services: cursor-based pagination, absence of secrets, operation replay, dry run, revision conflict, security settings replacement, rejection of out-of-schema arguments;
- smoke test through the real Host: `GET /api/mcp/default/tools` returns 21 tools (16 built-in and 5 application), and a chat under the model's control created another chat through `app_chats` and put a message in it through `app_runs`; the chat list in the open UI updated on the `data-changed` signal without reloading the page.
