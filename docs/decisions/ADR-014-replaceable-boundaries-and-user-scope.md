# ADR-014: Replaceable boundaries and the user scope

Status: Proposed

Date: 2026-10-22

## Context

The product is one Host process: `AI.Server` holds Domain, Application, Infrastructure and Hosting, Web
talks to it through `AI.Contracts`, tools run out of process through `AI.Mcp.BuiltIn` and `AI.Sandbox`.
Several directions are expected next, and each of them crosses the same code:

- several people using one Host at the same time;
- metadata (projects, chats, branches, settings) in a database rather than in JSON files;
- binary content (attachments, assets, large tool results) in S3 or another object store;
- more than one model protocol (OpenAI-compatible today; Anthropic Messages, OpenAI Responses, Ollama)
  and more than one agent harness;
- tools run somewhere other than a local confined process.

Ports exist for most of this already (`IChatRepository`, `IProjectRepository`, `IResourceRepository`,
`IGlobalSettingsRepository`, `IChatCompletionClient`, `IFileSystem`), and revision-checked saves map
directly onto a database row version. What is missing:

- **No notion of a user.** Every repository resolves its paths from one data root
  (`IProjectStorageLocation`, 17 consumers); settings, secrets, memory, skills and usage are global to
  the installation; change signals carry no owner.
- **Ports shaped by the file implementation.** `IChatRepository.SaveAsync` rewrites a whole
  `ChatThread`, `MayContainReviewReferenceAsync` is a file-scan hint, and the persistent and
  host-lifetime routers, the stamp caches and `DataDirectoryLock` sit next to the port instead of
  inside the JSON adapter.
- **Binary content mixed with metadata.** Whole tool results live inside chat documents, which is why
  `JsonChatRepository` needs a 96 MB read cache, and assets sit under the data root as plain files.
- **Model protocol and harness fused.** `ChatCompletionRequest` follows the OpenAI shape;
  `ChatAgent.RunAsync` (about 1,400 lines, about 30 dependencies) reports through about 15 callbacks,
  so another harness cannot be put in its place.

The constraint is cost: the change must not turn the product into a set of services or multiply the
code that every feature has to touch.

## Decision

### A modular monolith with six replaceable boundaries

The Host stays one process and one deployment. Replaceable adapters are introduced at exactly these
boundaries and nowhere else:

| Boundary | Port | First implementation | Expected next |
| --- | --- | --- | --- |
| User | `IUserContext`, `IUserScope` | the single local user | OIDC or cookie sign-in |
| Metadata: projects, chats, branches, settings | the `I*Repository` ports | JSON files | SQLite, then PostgreSQL |
| Binary content | `IBlobStore` | a directory | S3-compatible storage |
| Model protocol | `IModelClient` over a neutral message model | OpenAI-compatible | Anthropic Messages, Responses, Ollama |
| Agent harness | `IAgentHarness` with an event sink | `ChatAgent` | external agents, for example over ACP |
| Tool execution | `IToolEnvironment` | local process confined by `AI.Sandbox` | container or remote machine |

Rules that keep the cost down:

- **One implementation until a second is needed.** A port is introduced with the implementation in use
  today. A second one is written when a real consumer asks for it, not ahead of it.
- **Contract tests per port.** Each port gets one abstract test suite that every implementation runs
  (the way `FileSystemContractTests` covers `IFileSystem`). A second implementation is "done" when it
  passes the suite; that is what keeps two implementations cheap to maintain.
- **Adapters are Pure.DI setups.** `JsonStorage`, `SqlStorage`, `DirectoryBlobs`, `S3Blobs` and so on
  are internal setups that an executable composes with `DependsOn`, following
  [the Pure.DI conventions](../30-dependency-injection.md). Two profiles result: *Desktop* (one user,
  JSON, local files) and *Server* (many users, database, object store).
- **A separate assembly only for heavy dependencies.** An adapter that pulls a large package (AWS SDK,
  Npgsql) gets its own project so that Desktop does not ship it; everything else stays a folder in
  `AI.Server`.
- **Not introduced:** a generic repository, a mediator or CQRS framework, a message bus, event
  sourcing, or a split into services.
- **One Host instance.** Many users share one Host; horizontal scaling is not a goal. In-memory chat
  synchronization and the run registry stay valid, and no distributed lock is needed.

### Direction per boundary

- **Users.** A `UserId` owns projects and everything stored under them, plus per-user settings,
  secrets, memory, skills and usage. Background work (run recovery, schedules, chat kinds, subtasks)
  takes the user from what it stored, never from an HTTP request. Step 1 below is the detailed plan.
- **Settings.** One resolution order, *instance → user → project → chat → branch*, behind one resolver
  that also says which level each effective value came from. The levels are stored by the metadata
  provider, not in a standalone `settings.json`.
- **Metadata.** Chat ports follow the model, which is already an append-only graph: append messages,
  move a branch head with an expected revision, create a branch, and read headers and branch messages
  separately. The JSON adapter keeps its node files; a database maps this onto `chats`, `branches` and
  `messages` tables without rewriting a chat per agent step. File-only concerns (scan hints, routers,
  stamp caches, the directory lock) move inside the JSON adapter.
- **Binary content.** The project *workspace* stays a real directory where tools run: git, builds and
  editors need files, and an object store cannot stand in for them. Everything else that is bytes —
  attachments, assets, uploaded files, large tool results — goes to `IBlobStore` under content-addressed,
  immutable keys (SHA-256), and chats keep references. Chat documents shrink, and so does the need for
  the large read cache.
- **Model protocol.** A neutral message made of parts (text, image, tool call, tool result, reasoning,
  cache marks), one adapter per protocol, and an explicit capability description per connection (tools,
  images, reasoning, prompt caching, context window). Retrying and metering stay protocol-neutral
  decorators.
- **Harness.** The callbacks of `ChatAgent.RunAsync` become one stream of `AgentEvent` records written to
  an `IAgentRunSink`; `IAgentHarness.RunAsync(AgentRunRequest, IAgentRunSink, CancellationToken)` is the
  port. UI, history recording and run state consume the events and do not change when the harness does.
  Context planning, tool execution and result recording separate out of `ChatAgent` and
  `ChatRunDispatcher` along the way.
- **Tool execution.** `IToolEnvironment` says where the workspace is, how a process is started and which
  paths are granted. Tools reach the workspace only through it, never through Host paths, so runs of
  different users get different environments.

### Order

Each step leaves the product working and shippable on its own:

1. User scope with a single local user (below).
2. Harness events instead of callbacks, and the neutral model message.
3. `IBlobStore`; attachments, assets and large tool results move into it.
4. Append-oriented metadata ports, contract tests, then a SQLite provider.
5. Settings levels and the resolver.
6. `IToolEnvironment`, when the server profile needs it.

Steps 2–6 get their own detailed plan, recorded as an update of this ADR or a new one, when they start.

## Step 1: the user scope

### Goal

Every piece of stored data and every running operation belongs to a user, with exactly one user — the
local one — in both profiles. Behavior does not change for the person using the product. Afterwards,
adding sign-in is a matter of resolving another `UserId` per request; no repository, service or
background loop has to change again.

Not part of step 1: authentication, user management, per-user encryption keys, quotas, per-user
sandboxes, and the split between instance and user settings (all settings are per-user for now).

### Types

| Type | Layer | Lifetime | Purpose |
| --- | --- | --- | --- |
| `UserId` | Domain, next to `ProjectId` | value | `readonly record struct` over a `Guid`; `UserId.Local` is a fixed, well-known value |
| `IUserContext` | Application | — | `UserId Current { get; }`; throws when no scope is active |
| `IUserScope` | Application | — | `IDisposable Enter(UserId user)`; scopes nest and restore the outer user on dispose |
| `AmbientUserContext` | Application | Singleton | implements both over an `AsyncLocal<UserId?>` |
| `IRequestUserResolver` | Hosting | — | `UserId? Resolve(HttpContext context)` |
| `LocalUserResolver` | Hosting | Transient | always `UserId.Local`; it is reached only after the existing origin and bridge-token checks |
| `IUserDirectory` | Application | — | `ListAsync()`: the users whose data exists; returns `UserId.Local` alone in step 1 |
| `IInstanceStorageLocation` | Infrastructure | Singleton | the data root, for data that belongs to the installation |
| `IUserStorageLocation` | Infrastructure | Transient | `RootDirectory` = `<data root>/users/<userId:N>`, computed per call from `IUserContext` |

**Why ambient rather than a parameter.** The repositories are singletons with many consumers. Passing a
`UserId` through every port method and every service signature would touch most of `AI.Server` and every
future feature. An ambient scope keeps signatures and composition graphs unchanged, and the cost is
contained by two rules: the scope is entered only at the entry points listed below, and
`IUserContext.Current` **fails closed** — no scope is an exception, never a silent fall-back to the local
user — so a path that loses the scope is found by a test rather than by a leak.

### Storage layout

The data root keeps what belongs to the installation; everything else moves under the user:

```
<data root>/
  keys/                    instance: master key (FileMasterKeyStore)
  logs/                    instance: JsonLineFileLoggerProvider
  browser-access.txt       instance: BrowserAccessService (tokens map to UserId.Local in step 1)
  <lock and update files>  instance: DataDirectoryLock, HostUpdateService
  users/<userId:N>/
    settings.json  command-rules.json  credentials/  project-order.json  projects/
    memory/  instructions/  resources/  assets/  usage/  skills/
```

The desktop app's own files at the data root (`client-settings.json`, `theme.json`, `ui-state.json`,
`window.json`, `workspace-location.json`, `webview/`) belong to the desktop client, not to the Host, and
stay where they are.

The 17 consumers of `IProjectStorageLocation` split accordingly: `FileMasterKeyStore`,
`JsonLineFileLoggerProvider`, `DataDirectoryLock` and `BrowserAccessService` take
`IInstanceStorageLocation`; every repository, `GlobalSettingsPaths`, the storage-path classes,
`ResourceAssetService`, `SkillCatalog`, `JsonLinesTokenUsageLedger` and `WorkspaceUndoService` take
`IUserStorageLocation`. `IProjectStorageLocation` is removed so that no consumer stays ambiguous.

A singleton must not cache a path derived from the user root: `GlobalSettingsPaths.SettingsPath` is a
`{ get; } =` initializer today and becomes a computed property; every consumer is checked for the same
pattern. The read caches of `JsonChatRepository` and `JsonProjectRepository` are keyed by full path, so
they separate users without change; their memory budget is shared by all users, which is accepted.

**Relocation.** The product contains no relocation logic. Two scripts, `scripts/move-data-to-user.cmd`
and `scripts/move-data-to-user.sh`, take the data root as their argument and move the user entries
listed above into `users/<UserId.Local:N>/`. They are run once by hand while the Host is stopped. This is
a rename, not a format migration: no document changes. Each entry is moved on its own and an entry
already in place is skipped, so a run that was interrupted can simply be repeated. A Host started on a
data root that was not moved shows an empty installation and leaves the old entries untouched.
[Storage](../04-storage.md) describes the scripts.

### Entry points that enter the scope

| Entry point | Where the user comes from |
| --- | --- |
| HTTP and SSE requests | middleware after the existing origin and bridge checks: `IRequestUserResolver`; `401` when it returns `null` |
| Run recovery and chat-kind start-up (`ChatRunHostedService`, `WarmUpAsync`, `OnHostStartedAsync`) | each user from `IUserDirectory`, one scope per user |
| A run worker (`ChatRunDispatcher` `Task.Run(ProcessAsync)`) | the `UserId` the runtime captured when it was created; the worker enters it explicitly instead of relying on execution-context flow |
| Subtask reports, skill runs, reply suggestions | the owning runtime's `UserId`, entered explicitly |
| `ChatScheduler` loop | each user from `IUserDirectory`, one scope per pass |
| In-process app tools (ADR-007) | the `UserId` captured when the tool session is opened; the server's message loop is started in that scope, so every call it serves acts for that user whichever flow sends it |

The dispatcher's runtime records the `UserId` it was created for. `ChatRunState` itself does not carry
it: the run file already lives under the user's directory, so recovery knows the user from where it
read the file, and keeping the user next to the worker that needs it leaves the domain object and its
serialization unchanged. Reply drafts, kept in memory by chat and branch, record their user too and are
served only to it.

### Things that are global today and must be filtered

- **Run registry.** The dispatcher's runtimes are keyed by project, chat and branch ids, which are
  globally unique, so no two users collide — but an endpoint or tool that reads run state straight from
  the registry by id, or lists every run (`AppRunsTool`), must check ownership through the user's
  repository first or filter by the runtime's user. Each such site is listed and covered by a test.
- **Change and navigation signals.** `IAppDataChangeSignal.Notify()` and `IAppNavigationSignal` record the
  current user with each signal, and a subscription yields only its own user's signals.
- **Logs.** Every log scope carries `UserId`.

### Increments

Each increment is a commit that builds and passes `dotnet run --project build -- test-all`.

1. `UserId`, `IUserContext`, `IUserScope`, `AmbientUserContext`, `IRequestUserResolver`,
   `LocalUserResolver`, `IUserDirectory`, and the HTTP middleware. Storage still reads the data root;
   nothing observable changes.
2. `IInstanceStorageLocation` and `IUserStorageLocation` replace `IProjectStorageLocation`; computed
   paths instead of cached ones; the relocation scripts. Start-up recovery and chat-kind start-up enter
   each user's scope here already, because without it the Host could not recover its runs once storage
   depends on the user. In-memory caches of user data are keyed by path, so one user's entry cannot
   answer another's read.
3. Explicit scope at the background entry points; the runtime records its user; tool sessions capture the
   user; the scheduler passes over every user.
4. User-aware signals, the run-registry ownership checks, and `UserId` in log scopes.
5. Documentation: [Storage](../04-storage.md) layout, [Architecture](../02-architecture.md),
   [Concurrent chat runs](../14-concurrent-chat-runs.md), and
   [Implementation progress](../11-implementation-progress.md).

### Tests

- `AmbientUserContext`: `Current` without a scope throws; nested scopes restore the outer user; a scope
  survives `await` and `Task.Run`.
- Isolation, with a second `UserId` and the in-memory file system: projects, chats, settings, memory,
  skills, resources, usage and runs written by one user are invisible to the other, and an id of the
  other user's chat answers as absent.
- Background: recovery and the scheduler run each user's work under that user's scope; a run worker and
  an app-tool call see the run's user.
- Signals: a subscriber does not receive another user's changes or navigation requests.

### Done when

- no consumer of the data root is left that is neither instance nor user scoped;
- every entry point in the table enters a scope, and the full test suite passes with `Current`
  failing closed;
- an existing data directory moved by the script opens with all its projects and chats, checked by
  hand with the `.cmd` script on Windows and the `.sh` script on Unix;
- the two-user isolation tests pass.

## Consequences

Positive:

- sign-in, a database, an object store, a new protocol or a new harness each land behind one port,
  without touching features that do not use them;
- contract tests make a second implementation of a port a bounded piece of work;
- the user scope is added while there is one user and few call sites, which is its cheapest moment.

Negative and accepted:

- an ambient context is implicit: a loop that does not flow the execution context loses it. Failing
  closed and entering the scope explicitly at every background entry point is the mitigation, and new
  background loops must follow the same rule;
- existing installations need a manual step: the relocation script has to be run once before the
  upgraded Host is started, and it is not covered by the automated tests;
- the read-cache budget and the single Host instance are shared by all users; per-user quotas and
  horizontal scaling are left until they are needed.
