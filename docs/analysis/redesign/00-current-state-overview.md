# 00 — Current state of AI Client

Owner: Lead. Phase Ф3 of the redesign analysis: the synthesised current state, built from the four
area reports ([`01`](01-ai-models-current-state.md), [`02`](02-persistence-current-state.md),
[`03`](03-resources-current-state.md), [`04`](04-contracts-and-shared-parts.md)) and from the code
myself, at commit `7bb4d34` on branch `master`. Every claim here is traceable to a `path:line` in one
of those documents or in the source. Facts that are not verified in code are marked `assumption:` or
`external:`.

The purpose of this document is to state, in one place, *why* a three-component split is a real
change and not a repackaging: what the monolith actually is, which mechanisms cross every area, and
which of them are accidental rather than designed.

## 1. What the product is today

One .NET solution, one product, several executables and packages that all share one process at
runtime:

| Project | Role |
| --- | --- |
| `src/AI.Contracts` | Wire and shared types: chat, chats, projects, resources, runs, schedules, settings, skills, tools, usage, workspace, git, file system, memory, instructions, navigation, updates; plus `HostProtocol.cs` and the shared `Composition.cs` |
| `src/AI.Server` | Domain / Application / Infrastructure / Hosting — **everything else**: metadata storage, byte storage, the run dispatcher, model clients, the agent loop, tools, git, HTTP API, SSE |
| `src/AI.Web` | Blazor WASM UI served by the host |
| `src/AI.Desktop` | Desktop shell around the same host |
| `src/AI.Mcp.BuiltIn` | The built-in MCP tool server (files, processes, web, archives, triggers and C# scripts), launched as a child process over stdio |
| `src/AI.TextCorrection` | Separate helper |

There is exactly **one formal process boundary** in the product today, and it is already an HTTP one:
Host ↔ Web/Desktop, described by constants rather than by negotiation —
`ApiVersion = 1`, `PublicHostAddress = "http://127.0.0.1:52173/"`,
`PublicWebOrigin = "https://ai-dev-team.org"` ([`HostProtocol.cs:5-12`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Contracts/HostProtocol.cs#L5)),
with the HTTP surface declared by endpoint modules whose doc comment calls the route set the
Host-Web contract that "must not drift"
([`IEndpointModule.cs:9-12`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/Endpoints/IEndpointModule.cs#L9)).
The only other process boundary is the MCP stdio child process. Everything else — model calls,
metadata writes, blob writes, `git` invocations — happens in one process.

## 2. Process, lifetime and trust today

- **Addressing.** Loopback host, fixed port, fixed public web origin (`HostProtocol.cs:5-12`).
  There is no component identity anywhere on the wire: one `/api/health` answers
  `{ProductName, Version, ApiVersion, Status = "ready"}` and nothing distinguishes a liveness answer
  from a readiness one ([`HealthEndpoints.cs:12-21`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/Endpoints/HealthEndpoints.cs#L12)).
- **Trust.** The API trusts whoever can reach it. The read-only window over the host file system
  (`/api/filesystem/roots|directories|resolve`) is acceptable *only* because every allowed CORS
  origin is loopback and `ServerOptions.BrowseEnabled` can switch it off — the endpoint says so in
  its own comment
  ([`FileSystemEndpoints.cs:9-27`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/Endpoints/FileSystemEndpoints.cs#L9)).
  `assumption:` this is the single assumption that a multi-machine deployment invalidates first.
- **Data root.** `ServerOptions.DataDirectory` resolved once through `ProjectStorageLocation`,
  which calls `Path.GetFullPath` so no repository can drift to a relative path
  ([`ProjectStorageLocation.cs:13`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/ProjectStorageLocation.cs#L13));
  default `%LOCALAPPDATA%/AI`, overridden by `AI_CLIENT_DATA_DIRECTORY`, then by `--data-dir`, parsed
  in exactly one place ([`AiClientServer.cs:60-64`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/AiClientServer.cs#L60)).
- **Single writer.** `<root>/.lock` is opened with `FileShare.None` and held for the whole server
  lifetime ([`DataDirectoryLock.cs:13-26`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/DataDirectoryLock.cs#L13),
  [`AiClientServer.cs:23-33`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/AiClientServer.cs#L23)).
  A second process gets `DataDirectoryInUseException`. The interface states the reason plainly:
  repositories read a whole document, change it and write it back, so two processes would undo each
  other's writes
  ([`IDataDirectoryLock.cs:3-8`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/IDataDirectoryLock.cs#L3)).
- **Secrets.** Provider keys are encrypted with an OS-account-scoped protector (DPAPI on Windows;
  keyring or a data-directory key elsewhere), i.e. decryptable only by the same OS user on the same
  machine ([`Composition.cs:120-133`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Composition.cs#L120),
  `ProtectedGlobalSecretStore`).

## 3. Internal structure

Layering is by folder inside two assemblies, not by project: `AI.Server` holds Domain, Application,
Infrastructure and Hosting together, and `AI.Contracts` holds the DTOs of *every* future component
in one assembly. Composition is Pure.DI with one internal setup per assembly:
`AI.Contracts/Composition.cs` publishes the file-system contract for everyone, and
`AI.Server/Composition.cs` `DependsOn`s it, keeping its bindings grouped by lifetime and tag
([`Composition.cs:44-50`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Composition.cs#L44)).

Two consequences matter for the split:

1. The file-system contract is **not divisible**: whichever component owns it, the others must link
   the same setup, because re-binding it is a rejected override
   ([`Composition.cs:36-40`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Contracts/Composition.cs#L36)).
2. Long-lived state is created as singletons in one graph
   ([`Composition.cs:56-71`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Composition.cs#L56)):
   repositories with their write gates, the chat synchronization, the change signal, the run
   dispatcher, usage accounting, preview tickets, `ResourceAssetService` with its ticket dictionary,
   `WorkspaceFileSearch`, `WorkspaceChangeTracker`. Every one of these is a piece of state that a
   process boundary either moves, externalizes or deletes — they are the real inventory of the split.

## 4. The five seams

| Seam | What it is | Report |
| --- | --- | --- |
| `S1` | Talking to AI model providers: `IChatCompletionClient` and its chain, SSE parsing, transport policy, the agent loop, context planning and compaction, token/cost accounting | [`01`](01-ai-models-current-state.md) |
| `S2` | Metadata: data directory, single-writer guard, document schemas, revisions, repositories for projects, chats, branches, runs, settings, secrets, memory, instructions, schedules, usage ledger, chat search | [`02`](02-persistence-current-state.md) |
| `S3` | Bytes and paths: directory grants, file reading and search, diff capture and undo, attachments and artifacts, reviews, previews, scratch directories, and the `IFileSystem`/`IPath`/`IAtomicFileWriter` contract itself | [`03`](03-resources-current-state.md) |
| `S4` | Shared parts that must become reusable modules, and the component contracts `C1`–`C8` | [`04`](04-contracts-and-shared-parts.md) |
| `S5` | Orchestration and transport: the run dispatcher and its state machine, queues, SSE publication, the idempotency registry, the HTTP surface | [`04`](04-contracts-and-shared-parts.md), [`02`](02-persistence-current-state.md) |

Two structural observations come out of the seam tables and are worth stating once:

- **`S1` and `S3` are already joined inside the transport client.** The provider request body is
  built by reading image blobs through `IResourceAssetService` *inside* the model client
  ([`OpenAiCompatibleChatCompletionClient.cs:21`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Chat/OpenAiCompatibleChatCompletionClient.cs#L21),
  [`:271-274`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Chat/OpenAiCompatibleChatCompletionClient.cs#L271)) —
  a hidden `S1→S3` dependency that no interface names.
- **`S2` and `S3` are already joined inside one directory.** `assets/`, `resources/*.json` and
  `projects/*` share `ProjectStorageLocation.RootDirectory` under one lock
  ([`03` §3.3](03-resources-current-state.md)), which is why the byte store cannot move to an object
  store without splitting one consistency domain into two.

## 5. Where the data actually is

| Data | Location today | Addressing |
| --- | --- | --- |
| Projects, chats, branches | `<data>/projects/{projectId:D}.json`, `<data>/projects/{projectId:N}/chats/{chatId:N}.json` (+ `.summary.json`, `.context.json`) | by id, schema 2 / schema 9 (reads 5–9) |
| Runs and their queues | `<...>/{chatId:N}.{branchId:N}.run.json` | by id, schema 5, **no revision guard** — last writer wins |
| Schedules | `<...>/{chatId:N}.{branchId:N}.schedule.json` | by id, unversioned |
| Global settings, connections, MCP servers, tool policies | `<data>/settings.json` | single document, per-item compare |
| Secrets | `<data>/credentials/{scope}-{id:N}.protected` | OS-account-scoped encryption |
| Memory, project instructions | `<data>/memory/**`, `<data>/instructions/projects/{id:D}.json` | by id, schema 1 |
| Resource catalog, review catalog | `<data>/resources/{projectId:D}.json`, `<data>/resources/reviews-{projectId:D}.json` | by id, schema 1 — small JSON beside the blobs |
| Skills | `<data>/skills/**/SKILL.md` | paths under one root |
| Usage ledger | `<data>/usage/yyyy-MM.jsonl` | append-only JSON lines |
| Asset blobs and undo blobs | `<data>/assets/{projectId:N}/{assetId}.bin` + `.json`, `.../undo/blobs/{sha256}.bin` | content-addressed, **two different hash inputs** |
| Project files | wherever the directory grant points — outside the data directory | absolute OS path, link-resolved |
| Scratch of a run | `%TEMP%/AI.Client-<key>/<projectN>/<chatN>/` | absolute OS path |

## 6. Mechanisms that cross every area

These are the things the target design must keep working, and they are the reason a split cannot be
done area by area:

- **Identifiers and time.** UUIDv7 lower-case with dashes, minted by `Uuid7IdGenerator` over
  `Guid.CreateVersion7()` ([`Uuid7IdGenerator.cs:5-7`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Projects/Uuid7IdGenerator.cs#L5)),
  times as UTC ISO-8601. Sortability *by id* therefore depends on wall clocks.
  `external:` it requires bounded clock skew once components are on different machines.
- **Revisions.** Five different shapes coexist: `ProjectSaveResult.Conflict`, `ChatSaveResult.Conflict`,
  `MemoryWriteResult.Conflict`, a `(false, current)` tuple for instructions, and thrown
  `InvalidOperationException` in both catalogs, plus a per-item compare for settings; run state has no
  guard at all ([`02` §2.7](02-persistence-current-state.md)).
- **Serialization inside the process.** A private `AsyncGate` (a `SemaphoreSlim(1,1)`) per repository,
  held across read-modify-write, and — for chats — a per-chat semaphore and a per-chat write path.
  Twelve repositories use the pattern ([`02` §2.5](02-persistence-current-state.md)); in the catalogs
  the gate is per *repository instance*, so all projects serialize on one semaphore.
- **Change notification.** `AppDataChangeSignal` is an in-process `Channel<long>` with drop-oldest
  ([`AppDataChangeSignal.cs:7-30`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Notifications/AppDataChangeSignal.cs#L7)),
  feeding a 250 ms burst-damped SSE publication
  ([`RunEventsPublisher.cs:18-70`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/RunEventsPublisher.cs#L18)).
  A `long` counter in one process means nothing to another, so the *contract* of the signal survives
  but the counter must become one owned monotonic value.
- **Run idempotency.** Processed operation ids are persisted inside the run document
  ([`JsonChatRunRepository.cs:26-30`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/JsonChatRunRepository.cs#L26)),
  and the same mechanism produces deterministic response ids:
  `ReplyId(messageId) = SHA256("assistant:" + messageId)`,
  `PartialReplyId(messageId) = SHA256("assistant-partial:" + messageId)`
  ([`ChatRunDispatcher.cs:1360-1370`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Runs/ChatRunDispatcher.cs#L1360)).
  This is what makes a retried run produce one message instead of two, and it must survive the split
  unchanged.
- **Errors.** There is no component-neutral error envelope: handlers return ASP.NET results directly
  and one exception handler maps to status codes
  ([`ApiExceptionHandler.cs:6`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Hosting/ApiExceptionHandler.cs#L6)).
  Several real conflicts currently surface as `ArgumentException`, i.e. a 500.

## 7. The facts that decide the split

1. **Stored chat history contains absolute OS paths.** `ChatResource.Path` is documented as an
   absolute path for file/directory/diff kinds
   ([`ChatResource.cs:9-20`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Contracts/Resources/ChatResource.cs#L9)).
   A chat therefore names content by a location on one machine. Ten stored places carry absolute
   paths, and the grant's `CanonicalRoot` is the one where the path *is* the meaning
   ([`02` §3.8](02-persistence-current-state.md)).
2. **Two content-addressing schemes already coexist**: assets use `SHA256(bytes ‖ mediaType)`, undo
   blobs use `SHA256(bytes)`
   ([`ResourceAssetService.cs:47-54`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/ResourceAssetService.cs#L47),
   [`:133-134`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/ResourceAssetService.cs#L133)).
   Only the second is the object-store key the target wants.
3. **One writer per data directory is a lock, not a database** — the single constraint that makes
   "three components on three machines" impossible in the current design.
4. **The byte area is synchronous by construction.** Containment checks bridge a completed task with
   `GetAwaiter().GetResult()`
   ([`ProjectPathAccess.cs:17-19`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Resources/ProjectPathAccess.cs#L17))
   and `IResourceAssetService.ReadAsync` returns `byte[]`
   ([`IResourceAssetService.cs:6-8`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Resources/IResourceAssetService.cs#L6)).
   Both are correct only while nothing actually waits.
5. **Several capabilities are process-local state**: preview tickets and asset tickets are
   `ConcurrentDictionary` fields with in-process lifetimes
   ([`FilePreviewService.cs:33`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Resources/FilePreviewService.cs#L33),
   [`ResourceAssetService.cs:24`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Infrastructure/Storage/ResourceAssetService.cs#L24)),
   as are learned cost samples, context estimate samples and rate-limit windows.
6. **The agent loop fuses the model and the tools.** `ChatAgent` streams from the provider *and*
   executes tool calls in-process
   ([`ChatAgent.cs:31`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Tools/ChatAgent.cs#L31), [`:485`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Tools/ChatAgent.cs#L485));
   the natural model boundary is `IChatCompletionClient`, which is already exactly two operations
   ([`IChatCompletionClient.cs:4-14`](file:///C:/Projects/DevTeam/AI.Client/src/AI.Server/Application/Chat/IChatCompletionClient.cs#L4)).
7. **Secrets travel as a plain request field.**
   `ChatCompletionRequest.ApiKey` means a remote model service needs either the key on the wire or
   the decryption key locally — and the decryption key is OS-account-scoped.
8. **The transport client reads blobs while building the request** (fact 1 of §4), so a remote
   resource store adds its latency *inside* an outbound provider call.
9. **The project documentation and the code disagree about storage.** `docs/04-storage.md` describes
   message-per-file chat storage and schema 2; the code stores one document per chat with schema 9
   ([`02` §2.5](02-persistence-current-state.md)). Any design statement built on that document would
   be wrong, so this analysis is built on code references, and correcting the document is part of the
   migration path.
10. **Storage routing hides state.** `ChatRepositoryRouter`, `ChatRunRepositoryRouter` and
    `HostLifetimeChatRepository` keep some chats and runs in memory and never on disk, so "what is
    persisted" is a function of chat kind, not of a boundary
    ([`02` §2.8](02-persistence-current-state.md)).

## 8. Limits that must not be lost in translation

| Limit | Value |
| --- | --- |
| Attachment / captured content | 1 byte – 15 MiB |
| Text projected into the model | first 64 KiB |
| File excerpt in a message | 1 000 lines / 48 KiB |
| Captured `git diff` | 96 KiB, 200 untracked names |
| Undo snapshot per capture | 64 MiB |
| Resource references per message | 20 unique |
| Paths resolved per request | 100 |
| Provider transport budgets | 30 s headers / 90 s first token / 60 s content idle / 10 min retry deadline |
| Server-fault retries | 5, then give up; rate limits retried without a ceiling |
| Preview tickets / asset tickets | 4 096 entries / 4 h TTL, per process |

## 9. What follows from this for the design

The current state supports a three-component split, but only in a particular order of work:

1. Make grants logical and move the *decision* about them into the metadata owner, leaving physical
   access to a machine-bound seam — otherwise every stored path stays machine-specific.
2. Make the byte store content-addressed only, with one hashing rule, before any bytes move.
3. Keep the orchestrator (run state, queues, idempotency, SSE, the agent loop) with the metadata
   owner, so that "who is the source of truth for a run" has one answer.
4. Treat the machine-bound capabilities as an optional seam rather than as part of the byte store,
   so that the byte store can run where no project directory exists.

The design itself is in [`05-target-design.md`](05-target-design.md); the frozen decisions that
shape it are recorded there and in the team's lead branch.
