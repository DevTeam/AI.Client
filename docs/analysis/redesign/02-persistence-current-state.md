# Persistence of metadata: current state (seam S2)

Scope of this document: the seam `S2` — the storage of structured metadata in the AI Client monolith.
Owner: Bo · Хранилище. Part of the redesign analysis whose charter is the team charter of this chat.

Every statement about code carries a `path:line` reference to the repository at the working-tree state
displayed at writing time. Statements taken from `docs/` rather than from code are marked
`doc-only:`. Assumptions are marked `assumption:`.

## 1. Scope

In scope:

- the data directory, its default, its overrides and its layout;
- the single-writer guard on that directory and what happens to a second process;
- document formats, schema versions, atomic write order, recovery of interrupted writes;
- revisions and optimistic-conflict behaviour of every metadata repository;
- repositories for projects, chats and branches, runs and the run queue, global settings and
  connections, secrets, memory, project instructions, schedules, usage ledger, and the metadata
  part of resources and reviews;
- chat search over stored metadata;
- repository routers and path providers.

Explicitly out of scope (owned elsewhere in this analysis):

- model content: prompts, context building, compaction, transport (`S1`, Ada);
- file bytes and attachments: workspace file access, previews, asset bytes, undo snapshots (`S3`, Cleo);
- the HTTP API and event transport (`S5`).

## 2. Current structure

### 2.1 The data directory

`ServerOptions.DataDirectory` is the absolute root for "projects, chats, settings and logs"
(`src/AI.Server/Hosting/ServerOptions.cs:8`). It is resolved once into `ProjectStorageLocation`, which
calls `Path.GetFullPath` on it (`src/AI.Server/Infrastructure/Storage/ProjectStorageLocation.cs:13`),
so every repository reads the root from a single place and no repository can silently drift to a
relative path.

Precedence (documented in `docs/04-storage.md`, `doc-only:`): default `%LOCALAPPDATA%/AI`,
overridden by the `AI_CLIENT_DATA_DIRECTORY` environment variable, overridden by `--data-dir`.
The command line is parsed into `ServerOptions` before ASP.NET is built, so there is exactly one
parser (`src/AI.Server/Hosting/AiClientServer.cs:60-64`).

### 2.2 Single-writer guard

`DataDirectoryLock` opens `<root>/.lock` with `FileShare.None` — a sharing lock on Windows, `flock`
on macOS/Linux — and returns the open `FileStream` as the lifetime handle
(`src/AI.Server/Infrastructure/Storage/DataDirectoryLock.cs:13-26`). The lock is a direct platform
call on purpose; it is listed among the documented exceptions to `IFileSystem` (see
`docs/decisions/ADR-013-file-system-contract.md`, `doc-only:`).

The guard is taken first in `AiClientServer.StartAsync` and held for the whole server lifetime
(`src/AI.Server/Hosting/AiClientServer.cs:23-33`), before any repository is built; on a failed start
it is disposed again (`:33-36`). A second process on the same directory gets `DataDirectoryInUseException`
(`src/AI.Server/Infrastructure/Storage/DataDirectoryInUseException.cs:3`) with the message pointing at
`--data-dir` (`.../DataDirectoryLock.cs:22-25`). The implementation comment states why the lock file is
not deleted on close: deleting it would let a second process lock a fresh file while a third still
holds the unlinked one (`.../DataDirectoryLock.cs:7-10`).

The interface documents the real reason the guard exists: repositories read a whole document into
memory, change it, and write it back whole, so two processes over the same files would undo each
other's changes (`src/AI.Server/Infrastructure/Storage/IDataDirectoryLock.cs:3-8`).

### 2.3 Directory layout on disk

Resolved from `IProjectStorageLocation.RootDirectory` plus literal segments. Path providers:

| Provider | Location |
| --- | --- |
| `ProjectStoragePaths` | `<root>/projects/{projectId:D}.json` |
| `ChatStoragePaths` | `<root>/projects/{projectId:N}/chats/{chatId:N}.json`, `.summary.json`, `.context.json` |
| `ChatRunStoragePaths` | `<root>/projects/{projectId:N}/chats/{chatId:N}.{branchId:N}.run.json` |
| `GlobalSettingsPaths` | `<root>/settings.json`, `<root>/credentials/{scope}-{id:N}.protected` |

Note the inconsistency that already exists: project documents are named with the dashed `D` format
because the path interpolates the `ProjectId` struct, whose `ToString()` is `Value.ToString("D")`
(`src/AI.Server/Infrastructure/Storage/ProjectStoragePaths.cs:9`,
`src/AI.Server/Domain/Projects/ProjectId.cs:19`), while chats and runs use the compact `N` format
because they interpolate `Value` with `:N` (`.../ChatStoragePaths.cs:9-13`,
`.../ChatRunStoragePaths.cs:5`). `ChatId` has no `ToString()` override at all
(`src/AI.Server/Domain/Chats/ChatId.cs`), so a chat id can only be rendered by naming the format
explicitly.

Repositories that build their own paths rather than using a provider:

| Repository | Path | Reference |
| --- | --- | --- |
| `JsonGlobalSettingsRepository` | `<root>/settings.json` (+ `.tmp`) | `src/AI.Server/Infrastructure/Settings/JsonGlobalSettingsRepository.cs:55-59` |
| `ProtectedGlobalSecretStore` | `<root>/credentials/{scope}-{id:N}.protected` | `src/AI.Server/Infrastructure/Settings/GlobalSettingsPaths.cs:9-10` |
| `JsonResourceRepository` | `<root>/resources/{projectId:D}.json` | `src/AI.Server/Infrastructure/Storage/JsonResourceRepository.cs:78` |
| `JsonReviewRepository` | `<root>/resources/reviews-{projectId:D}.json` | `src/AI.Server/Infrastructure/Storage/JsonReviewRepository.cs:86` |
| `JsonMemoryRepository` | `<root>/memory/user.json`, `<root>/memory/projects/{projectId:D}.json` | `src/AI.Server/Infrastructure/Storage/JsonMemoryRepository.cs:92-93` |
| `JsonProjectInstructionsRepository` | `<root>/instructions/projects/{projectId:D}.json` | `src/AI.Server/Infrastructure/Storage/JsonProjectInstructionsRepository.cs:58` |
| `JsonLinesTokenUsageLedger` | `<root>/usage/yyyy-MM.jsonl` | `src/AI.Server/Infrastructure/Storage/JsonLinesTokenUsageLedger.cs:90,94` |
| `SkillCatalog` | `<root>/skills/user/{id}/SKILL.md`, `<root>/skills/projects/{projectId:D}/{id}/SKILL.md` | `src/AI.Server/Infrastructure/Storage/SkillCatalog.cs:106-107,110-111` |
| `ChatScheduleStore` | `<root>/projects/{projectId:N}/chats/{chatId:N}.{branchId:N}.schedule.json` | `src/AI.Server/Application/Schedules/ChatScheduleStore.cs:116-118` |
| `FileMasterKeyStore` | `<root>/keys/...` (cleo/ada seam, listed for completeness) | `src/AI.Server/Infrastructure/Credentials/FileMasterKeyStore.cs:14` |

Consequence for the redesign: the layout is already "one root, two independent naming schemes per id
kind", and three repositories (`JsonResourceRepository`, `JsonReviewRepository`,
`JsonProjectInstructionsRepository`, `JsonMemoryRepository`) reach into `IProjectStorageLocation`
directly instead of going through a path provider.

### 2.4 Entity → file → schema → writer → serializer

| Entity | File(s) | Schema | Who writes | Serializer |
| --- | --- | --- | --- | --- |
| Project | `<root>/projects/{id:D}.json` | 2 | `JsonProjectRepository` | `ProjectDocumentSerializer` |
| Chat (manifest+transcript) | `<root>/projects/{pid:N}/chats/{cid:N}.json` | 9 (reads 5, 6, 7, 8, 9) | `JsonChatRepository`, `HostLifetimeChatRepository` | `ChatDocumentSerializer` |
| Chat summary | `<...>/{cid:N}.summary.json` | 9 (same reader set) | same two | `ChatDocumentSerializer.SerializeSummary` |
| History checkpoints | `<...>/{cid:N}.context.json` | unversioned `HistoryCheckpoint[]` | `JsonHistoryCheckpointRepository` | `System.Text.Json` inline |
| Run state + queue | `<...>/{cid:N}.{bid:N}.run.json` | 5 | `JsonChatRunRepository`, `HostLifetimeChatRunRepository` | inline record in the repository |
| Schedule | `<...>/{cid:N}.{bid:N}.schedule.json` | unversioned `ChatSchedule` | `ChatScheduleStore` | `System.Text.Json` (web defaults) |
| Global settings, connections, MCP servers, tool policies | `<root>/settings.json` | unversioned `GlobalSettings` | `JsonGlobalSettingsRepository` | `System.Text.Json` |
| Secrets (connection key, MCP env, MCP credential) | `<root>/credentials/{scope}-{id:N}.protected` | n/a (base64 of protected bytes) | `ProtectedGlobalSecretStore` | none; `IUserDataProtector` |
| Memory entries | `<root>/memory/user.json`, `<root>/memory/projects/{pid:D}.json` | 1 | `JsonMemoryRepository` | inline record |
| Project instructions | `<root>/instructions/projects/{pid:D}.json` | 1 | `JsonProjectInstructionsRepository` | inline record |
| Resource catalog | `<root>/resources/{pid:D}.json` | 1 | `JsonResourceRepository` | inline record |
| Review catalog | `<root>/resources/reviews-{pid:D}.json` | 1 | `JsonReviewRepository` | inline record |
| Skill documents | `<root>/skills/.../SKILL.md` | n/a (Markdown with metadata) | `SkillCatalog` | `SkillMarkdown` |
| Token usage ledger | `<root>/usage/yyyy-MM.jsonl` | unversioned record per line | `JsonLinesTokenUsageLedger` | `System.Text.Json`, camelCase, enum-as-string |
| Undo manifests | `<root>/assets/{pid:N}/undo/{id:N}.json` | 1 | `WorkspaceUndoService` (S3 seam) | `System.Text.Json` |

Schema versions are hard-coded constants, one per serializer:
`ProjectDocumentSerializer.cs:10` (2), `ChatDocumentSerializer.cs:13-14` (9 and previous 6),
`JsonChatRunRepository.cs:14` (5). Catalog repositories declare their version inside the anonymous
document record and reject anything else (`JsonResourceRepository.cs:55`, `JsonReviewRepository.cs:72`,
`JsonMemoryRepository.cs:84`, `JsonProjectInstructionsRepository.cs:55`).

### 2.5 How the documents are written

Every persistent repository owns a private `AsyncGate` — a `SemaphoreSlim(1,1)` lease
(`src/AI.Server/Infrastructure/Storage/AsyncGate.cs:5-16`) — and holds it across read-modify-write:

| Repository | Gate field |
| --- | --- |
| `JsonProjectRepository` | `.../JsonProjectRepository.cs:15` |
| `JsonChatRepository` | `.../JsonChatRepository.cs:17` |
| `JsonChatRunRepository` | `.../JsonChatRunRepository.cs:13` |
| `JsonGlobalSettingsRepository` | `.../JsonGlobalSettingsRepository.cs:12` |
| `JsonMemoryRepository` | `.../JsonMemoryRepository.cs:15` |
| `JsonProjectInstructionsRepository` | `.../JsonProjectInstructionsRepository.cs:15` |
| `JsonResourceRepository` | `.../JsonResourceRepository.cs:11` |
| `JsonReviewRepository` | `.../JsonReviewRepository.cs:11` |
| `JsonHistoryCheckpointRepository` | `.../JsonHistoryCheckpointRepository.cs:25` |
| `JsonLinesTokenUsageLedger` | `.../JsonLinesTokenUsageLedger.cs:28` |
| `SkillCatalog` | `.../SkillCatalog.cs:13` |
| `WorkspaceUndoService` | `.../WorkspaceUndoService.cs:20` |

`JsonChatRunRepository.GetAsync` and `JsonChatRepository.GetAsync` read without taking the gate
(`.../JsonChatRunRepository.cs:17-21`, `.../JsonChatRepository.cs:71-90`) — safe only because the
final move is atomic.

The write pattern is uniform: write the full document to a sibling temporary file, then move it over
the target with overwrite semantics.

- Project: `WriteTextAsync(.tmp)` → `MoveAsync(.tmp, path, overwrite: true)`
  (`.../JsonProjectRepository.cs:57-61`).
- Chat: writes both the transcript and the summary, then moves the transcript, then the summary
  (`.../JsonChatRepository.cs:182-192`).
- Run, settings, memory, instructions, resources, reviews, checkpoints, skills, undo manifest: same
  temporary-then-move shape (`.../JsonChatRunRepository.cs:23-34`,
  `.../JsonGlobalSettingsRepository.cs:55-59`, `.../JsonMemoryRepository.cs:86-91`,
  `.../JsonProjectInstructionsRepository.cs:34-38`, `.../JsonResourceRepository.cs:58-63`,
  `.../JsonReviewRepository.cs:76-81`, `.../JsonHistoryCheckpointRepository.cs:45-48`,
  `.../SkillCatalog.cs:67-69`).

The chat case is the only one where two files are updated together and the order is chosen
deliberately: transcript first, then summary. A crash between the two leaves a summary that is one
revision behind, which the list path repairs lazily (`.../JsonChatRepository.cs:33-68`).

`docs/04-storage.md` describes a different chat layout — messages written separately to
`<chatPath>.nodes/<messageId>.json`, immutable nodes written first and a manifest replaced after
(`doc-only:`). No code in the repository performs this; the transcript is one document and messages
are an array inside it (`ChatDocumentSerializer.cs:26-38`, `JsonChatRepository.cs:171-193`). The
nearest equivalent today is the summary file plus the "immutable nodes are never modified" rule that
now lives only in a comment. **This is the largest divergence between the documented and the actual
state, and the redesign must be described against the code, not against that document.**

### 2.6 Interrupted-write recovery

Only `JsonProjectRepository` has an explicit recovery step, `RecoverAsync`, run before both reads and
writes while the gate is held (`.../JsonProjectRepository.cs:85-101`):

- no `.tmp` → nothing to do;
- `.tmp` and target both exist → delete the `.tmp` (the target won the race);
- `.tmp` only → move it into place.

No other repository has an equivalent. For chats, a leftover `.tmp` is simply overwritten by the next
save. For the usage ledger, a torn last line is skipped at parse time rather than repaired
(`.../JsonLinesTokenUsageLedger.cs:63-79`), and for the checkpoint file a damaged document degrades
to "no checkpoints" instead of failing the chat (`.../JsonHistoryCheckpointRepository.cs:60-68`).

### 2.7 Revisions, conflicts and idempotency

| Repository | Guard | On mismatch |
| --- | --- | --- |
| Project save/delete | `expectedRevision` from the caller, compared against the stored `Revision` | `ProjectSaveResult.Conflict(currentRevision)` (`JsonProjectRepository.cs:50-55,74-82`) |
| Chat save/delete | same | `ChatSaveResult.Conflict(revision)` (`JsonChatRepository.cs:175-180,201-205`) |
| Memory update/delete | same | `MemoryWriteResult.Conflict(current)` (`JsonMemoryRepository.cs:39-41,54-56`) |
| Instructions save | same | returns `(false, current)` (`JsonProjectInstructionsRepository.cs:31-32`) |
| Resources retire | same | throws `InvalidOperationException` (`JsonResourceRepository.cs:38-39`) |
| Reviews update | same | throws `InvalidOperationException` (`JsonReviewRepository.cs:41-42`) |
| Skills save/delete | same | `"Conflict"` result (`SkillCatalog.cs:60-61,83-84`) |
| Run state | none — last writer wins | n/a |
| Global settings | no document revision; per-item compare against the item the editor read | `SettingsConflictException` (`GlobalSettingsService.cs:52-57`, `SettingsConflictException.cs`) |

Reads that must report "not there" return `null` instead of throwing — a deliberate contract of
`IFileSystem`, which the repositories rely on (`.../JsonProjectRepository.cs:20-21`,
`.../JsonChatRepository.cs:72`, `.../JsonResourceRepository.cs:48-49`).

Chat runs additionally carry an idempotency list: `ChatRunState.Operations` holds processed
`OperationId` values and is persisted with the run document
(`JsonChatRunRepository.cs:26-30` and the `Operations` field of the document record at `:105-110`).
`docs/04-storage.md` records the intent — a command stays in the queue until the response is
committed, retries reuse the original message id and a deterministic response id (`doc-only:`).

### 2.8 Storage-lifetime routing

Two kinds of chat persistence coexist and are chosen by chat kind:

- `ChatRepositoryRouter` routes by `IChatKindPolicyRegistry.Resolve(kind).Behavior.Persistence`
  (`src/AI.Server/Infrastructure/Storage/ChatRepositoryRouter.cs:49-50`); reads try the host-lifetime
  store and fall back to the durable one (`:24-34`). The registry is resolved through a `Func<>` on
  use because resolving it during construction can complete a cycle through the chat services
  (`:8-11`).
- `HostLifetimeChatRepository` keeps serialized chat documents and their summaries in a
  `ConcurrentDictionary` and never touches the disk (`:53-110`).
- `ChatRunRepositoryRouter` mirrors this for run state, and caches the resolved location per
  `(projectId, chatId)` (`src/AI.Server/Infrastructure/Storage/ChatRunRepositoryRouter.cs:15,87-92`);
  when it cannot find a chat to resolve the kind it throws
  `InvalidOperationException("Chat not found for run storage.")` (`:89-91`).

## 3. Data and formats

### 3.1 Project document (schema 2)

`ProjectDocument` = `SchemaVersion, Revision, Id, Name, Description, CreatedAt, UpdatedAt,
DirectoryGrants[], McpServers[], ToolPolicies[], ConnectionId?`
(`src/AI.Server/Infrastructure/Storage/ProjectDocumentSerializer.cs:110-121`). The deserializer
rebuilds the domain object grant by grant, server by server, policy by policy
(`:69-107`), so validation of each nested item happens through the domain constructors rather than
through the serializer. Unknown schema version and negative revision throw `JsonException`
(`:59-66`).

Directory grants and MCP bindings are part of the project document, i.e. the security and tool
configuration travels with metadata — but the actual filesystem access they describe is `S3`.

### 3.2 Chat document (schema 9)

The chat document is the largest metadata document. Its record carries the chat header (id, project,
title, timestamps, connection override, pin/archive state, approval mode, kind and kind state) plus
`Messages[]`, `Branches[]` and `ToolPolicies[]`
(`src/AI.Server/Infrastructure/Storage/ChatDocumentSerializer.cs:17-47,164-192`).

Each `ChatMessageDocument` carries content, role, parent id, tool calls, tool-call id, workspace
changes, resource references, delivery mode and sender
(`:273-285`). `WorkspaceChangeDocument` embeds a **diff per changed file** directly into the chat
document, with additions, deletions, previous path and the diff text (`:257-271`). This is metadata
whose size grows with code, not with conversation: it is the main reason a chat document can become
tens of megabytes, and it is a seam with `S3`.

`ChatSummaryDocument` deliberately excludes `Messages`, so the sidebar listing never materialises the
transcript (`:193-215`, and the comment at `:195-198`). It carries `BranchCount` as `int?` precisely
so that a manifest written before the field existed can be recognized and migrated, and `IsEmpty`
with the same "absent reads as the old behaviour" trick (`:199-212`).

The reader accepts versions 5, 6, 7, 8 and 9 (`:67`, `:139`) and additionally maps pre-`Kind`
documents through `LegacyKind` (`:160-162`). Chat manifests are the only documents in the system with
real backwards tolerance; projects, runs and all catalogs are strict.

A chat also has a summary-side migration path: `ListSummariesAsync` re-reads a stale manifest and
rewrites it, taking the write gate and re-checking after acquiring it because a save may have
completed in between (`JsonChatRepository.cs:26-64`).

### 3.3 Run document (schema 5)

`Document` = `SchemaVersion, ProjectId, ChatId, BranchId, Status, StreamingContent, Error,
FailureKind, HasUnreadResponse, Revision, Queue[], Operations[]`
(`src/AI.Server/Infrastructure/Storage/JsonChatRunRepository.cs:95-110`). `StreamingContent` means
the **partially generated model response is persisted into the run document as it streams**. The
reader validates that all three ids are non-empty, that `Queue` and `Operations` are present, and
that every enum value is defined (`:71-80`). Run documents are the highest-traffic writes in the
system, and there is one per `(projectId, chatId, branchId)`.

Deletion by pattern is used for cleanup: `DeleteProjectAsync` removes `*.run.json` under the project,
`DeleteChatAsync` removes `{chatId:N}.*.run.json`, `DeleteExceptAsync` keeps a set of branches and
re-reads each file to find its branch id (`:46-69`).

### 3.4 Global settings and secrets

`GlobalSettings` = `Connections[], McpServers[], ToolPolicies[], ChatAutomation?, Sandbox?`, in
`AI.Contracts.Settings` (`src/AI.Contracts/Settings/GlobalSettings.cs:3-8`). There is no schema
version and no document revision; compatibility is handled field by field at load
(`JsonGlobalSettingsRepository.cs`), and the loader **injects the two host-provided MCP
servers** into the returned value if the stored document does not list them yet. It also drops the
retired optional C# scripting server's entry and moves its `cs_run` policies to the built-in server
(`LegacyCSharpToolMigration`); project and chat documents get the same move when they are read.
Conflict detection is per item, by comparing the item the editor read with the stored one
(`GlobalSettingsService.cs:52-57,59-67`), which is a different concurrency model from the revision
number used everywhere else.

Secrets never travel with settings. The document stores only `HasCredential`/`HasSecret` flags, the
service computes them from the secret store on every read (`GlobalSettingsService.cs:69-85`), and the
bytes live in a separate `<root>/credentials/...` file protected by `IUserDataProtector`
(`ProtectedGlobalSecretStore.cs:13-33`). Scopes in use: `connection`, `mcp`, `mcp-env`
(`GlobalSettingsService.cs:104,183,254`). MCP environment secrets are stored as one JSON object per
server id (`:257-260`).

`ChatAutomation` and `Sandbox` are fields of global settings that the settings editor does not own; they are read back
from storage and written unchanged on every settings save (`GlobalSettingsService.cs:139-142`), with
the comment that the request carries only what the editors own.

### 3.5 Catalogs: memory, instructions, resources, reviews

All four are one small document per scope, each holding the whole catalog as an array, with a version
constant and the temporary-then-move write. `JsonMemoryRepository` covers both `user.json` and the
per-project files with a **single gate for all catalogs**, justified in its comment by their small
size and rare writes (`JsonMemoryRepository.cs:10-14`). It also enforces a capacity limit and returns
a rejected result rather than throwing (`:29-34`).

`JsonProjectInstructionsRepository` is kept as a separate document from the project on purpose, so
editing instructions never races a security or connection change
(`JsonProjectInstructionsRepository.cs:9-11`).

**Resource and review catalogs (decision D2).** Both belong to metadata, not to bytes, and both are a
single per-project document. `JsonResourceRepository` holds `<root>/resources/{projectId:D}.json`
(`JsonResourceRepository.cs:78`) and `JsonReviewRepository` holds
`<root>/resources/reviews-{projectId:D}.json` (`JsonReviewRepository.cs:86`); neither goes through a
path provider — both compose the path themselves from `IProjectStorageLocation.RootDirectory`, which
is seam `S2-3`.

Who reads and writes them:

| Catalog | Written by | Read by |
| --- | --- | --- |
| Resource catalog | `JsonResourceRepository.GetOrCreateAsync` / `RetireAsync` / `DeleteProjectAsync`, called only from `ResourceService` (`ResourceService.cs:16,35`), i.e. one writer through one service | the same service; the catalog is the dedup table for `@` references, not the source of truth for a message's references |
| Review catalog | `JsonReviewRepository.CreateAsync` / `UpdateAsync` / `DeleteAsync` / `DeleteChatAsync`, through `ReviewService` (`ReviewService.cs:8,27,62,97,101`) | `ReviewService.ListAsync` / `GetAsync` |

Locking and atomicity:

- each repository owns **one `AsyncGate` per instance**, not per project, held across the whole
  read-modify-write (`JsonResourceRepository.cs:11,26,74-75`; `JsonReviewRepository.cs:11,82-83`), so
  every catalog of every project serialises on the same semaphore — the opposite choice from
  `JsonMemoryRepository`, which documents its single gate explicitly (`JsonMemoryRepository.cs:10-14`);
- both write by temporary file plus `MoveAsync(..., overwrite: true)`
  (`JsonResourceRepository.cs:74-75`, `JsonReviewRepository.cs:82-83`), like every other repository;
- both throw `InvalidOperationException` on a revision mismatch rather than returning a conflict
  result (`JsonResourceRepository.cs:38-39`, `JsonReviewRepository.cs:39`), a fourth revision shape on
  top of the three in `S2-8`;
- the blob side is **not** under those gates. `ResourceAssetService` writes content-addressed blobs and
  their media-type sidecar through `IAtomicFileWriter` (`ResourceAssetService.cs:59-64`), so a catalog
  write and the blob write it refers to are never one transaction.

How they relate to the bytes:

- `ChatResource.AssetId` is the blob key: the SHA-256 of the bytes concatenated with the media type,
  lowercase hex (`ResourceAssetService.cs:47-54`), stored as
  `<root>/assets/{projectId:N}/{assetId}.bin` plus `{assetId}.json` for the media type
  (`ResourceAssetService.cs:32-34,59-64`). Identical uploads therefore land on one blob.
- Undo blobs use a *second*, looser addressing rule under the same tree: the SHA-256 of the raw bytes
  only, no media type (`ResourceAssetService.cs:133-134`, directory at `:36-37`). Two schemes for one
  idea — worth one scheme in the design.
- The address is **per project**, not global (`AssetDirectory(projectId)`), which is the only thing
  that keeps identical content in two projects separate.
- `ResourceAssetService` also keeps process-local state: a ticket table with a four-hour expiry
  (`ResourceAssetService.cs:24,116-119`). Like `HostLifetimeChatRepository`, it is invisible state
  inside storage.
- The catalog duplicates what the transcript already holds. `ChatReview.SourceChanges` is a full
  `WorkspaceChangeSet` — file paths **and diff bodies** — persisted inside the review catalog
  (`ChatReview.cs:5-13`; `ReviewService.cs:54`), and `ListAsync` simply nulls it out for display
  (`ReviewService.cs:16`). The same bytes are therefore stored twice per reviewed turn.
- Resource dedup keys on the path string: an existing entry is reused when kind and `Path` match,
  with Windows case-insensitive comparison (`JsonResourceRepository.cs:13-14,29-30`). That makes the
  absolute path part of the catalog's identity, not only of its content.

### 3.6 Usage ledger

Append-only JSON lines, one file per month, camelCase with string enums
(`JsonLinesTokenUsageLedger.cs:22-31,90-94`). Months are parsed once and cached in memory
(`:26,44-53`). Reads filter by month prefix before parsing, so a report over a recent period does not
read years of history (`:48-53`).

### 3.7 Chat search

`ChatSearchService` scans stored chats sequentially; there is no index, and the comment states this
is deliberate (`src/AI.Server/Application/Chats/ChatSearchService.cs:11-20`). Ordering is by
`(projectId, chatId)` so that paging cursors stay stable (`:135-152`), and every cost limit is
explicit — match count, character budget, examined-message ceiling (`:57-62,70-74`). A cursor is
literally `chatIndex:messageIndex` (`:186-198`). This is a **whole-corpus read** in the current
process; across a network boundary it becomes the most expensive operation in `S2`.

### 3.8 Absolute paths in stored formats

Decision `D1` moves access decisions into `ai-core` as logical, project-relative grants and moves the
bytes to `ai-resources`; decision `D3` asks which formats carry an absolute path today. This is the
inventory. No format stores a relative path anywhere.

| # | Format (schema) | Field | Shape today | Reference |
| --- | --- | --- | --- | --- |
| P1 | Project (2) | `DirectoryGrants[].CanonicalRoot` | absolute canonical OS path; canonicalised again on write | `ProjectDocumentSerializer.cs:29,128-132`; `ProjectEndpoints.cs:57-58` |
| P2 | Chat (9) | `Messages[].Resources[].Path` for `File`, `Directory`, `Diff` | absolute canonical path (the repository root for `Diff`) | `ChatDocumentSerializer.cs:286`; `ResourceService.cs:230,235,301` |
| P3 | Chat (9) | `Messages[].Resources[].Path` for `Image`, and for `File` with an `AssetId` | the original source location — an upload's client path or the workspace path — i.e. display metadata, plus the blob key in `AssetId` | `ChatResource.cs:12-23`; `ResourceAssetService.cs:65` |
| P4 | Chat (9) | `Messages[].WorkspaceChanges.Files[].Path` and `.PreviousPath` | absolute canonical path | `ChatDocumentSerializer.cs:263-271`; `WorkspaceChangeTracker.cs:314-320` |
| P5 | Chat (9) | `Messages[].ToolCalls[].Arguments` | opaque tool JSON as the model wrote it; usually contains absolute paths | `ChatToolCall.cs:3`; `ChatDocumentSerializer.cs:281` |
| P6 | Resource catalog (1) | `ResourceDefinition.Reference` (the whole `ChatResource`) **and the dedup key** | absolute path, compared case-insensitively on Windows | `JsonResourceRepository.cs:13-14,29-30` |
| P7 | Review catalog (1) | `ChatReview.Files[]`, `ReviewComment.Path`, `SourceChanges.Files[].Path` / `.PreviousPath` | absolute paths copied verbatim out of the chat document | `ChatReview.cs:5,17-20`; `ReviewService.cs:54,126,153` |
| P8 | Undo manifest (1) | `UndoFile.Path` | absolute, and the manifest is *defined* in terms of it: the restore checks `Path.IsPathFullyQualified`, re-derives the directory and writes there | `WorkspaceUndoService.cs:196,160-162,205` |
| P9 | Global settings | `McpServerSettings.Command`, `.Arguments`, `.WorkingDirectory` | machine-specific command lines, node configuration rather than entity metadata | `McpServerSettings.cs:6-14` |
| P10 | API-only, not stored | `ResolvedPath.Path`, `ResourceSuggestion.Path`, `WorkspaceDiffSource.Path` | absolute, produced per request | `ResolvePathsRequest.cs:12-15`; `ResourceSuggestion.cs:5-17` |

Notes that matter for the migration:

- **P1 is the grant itself**, so it is the one format that cannot simply be rewritten in place: the
  absolute root *is* the grant's meaning today. `DirectoryGrant` rejects an empty root
  (`DirectoryGrant.cs:22-26`), and every consumer reads `CanonicalRoot` as a real directory:
  `ProjectPathAccess.AccessOf` (`ProjectPathAccess.cs:26-29`), `WorkspacePathResolver`
  (`WorkspacePathResolver.cs:20-23`), `ChatAgent` handing it to the tool session as
  `ToolDirectoryGrant.Root` (`ChatAgent.cs:93-94`), `WorkspaceFileSearch`
  (`WorkspaceFileSearch.cs:102`), `SkillCatalog`-side suggestions and the risk-assessment skill
  (`ChatToolRiskAssessSkill.cs:90-92`). Under `D1` the stored value must become a logical root and the
  absolute directory must become a per-node binding.
- **P2/P4 are the same path twice** for a touched file: once as a reference and once inside the change
  set, written by two unrelated code paths that both canonicalise through
  `IProjectPathAccess.ResolveLinks` (`ResourceService.cs:230,301`;
  `WorkspaceChangeTracker.cs:314-320`).
- **A reference may name a path that is inside no grant.** `WorkspacePathResolver.FindAsync` returns a
  resolved absolute path even when access is `None`, deliberately, so the person can be offered a
  grant (`WorkspacePathResolver.cs:52-54`; `ResolvePathsRequest.cs:12-15`). Such a reference has no
  grant id and no relative path to migrate to.
- **P5 is not mechanically migratable.** Tool arguments are the model's own text; rewriting paths
  inside them means parsing every tool's schema. It should be declared as history that keeps its
  original spelling.
- **P7 duplicates P2 and P4 data**, so a migration must rewrite the review catalog with the same rules
  as the chat document, and the review's file list must keep matching `SourceChanges.Files[].Path`
  or `ValidateFiles` starts rejecting saved reviews (`ReviewService.cs:122-127`).
- **P8 has an ordering requirement**: an undo manifest is only usable where the absolute path still
  resolves, so its migration cannot be deferred past the move to another machine.
- `ChatResourceSource.Upload` carries a path from the client's machine (`ChatResource.cs:40`), which is
  never valid anywhere else and must survive only as a display string.

### 3.9 What the path migration has to produce

For `D1`/`D3`, the fields above become one of three things:

| Keep | Replace with | Applies to |
| --- | --- | --- |
| nothing | `{ grantId, relativePath }`, `/`-separated, case-preserving | P2, P4, P6, P7, and P8 once the node has resolved the grant |
| nothing | `{ blobId }` plus the existing `Name`, `MediaType`, `Size`, `Excerpt`, `Lines` | the diff bodies of P4 and of `ChatReview.SourceChanges`, and P3's stored bytes |
| a display-only copy | `legacyAbsolutePath` (write-once, never resolved) | P3, P5, and any P2/P4 path that fell outside every grant |

`WorkspaceChangeSet` in the chat document and `ReviewComment.Path` anchoring into a diff
(`ReviewService.cs:150-170`) both depend on the *same string* being used in the file list, the change
set and the comments, so the three must be rewritten in one pass or reviews lose their anchors.

## 4. Seams and coupling

| Seam | Interface / type | File:line | Affected areas | Seam exists today | Proposal |
| --- | --- | --- | --- | --- | --- |
| S2-1 Data root | `ServerOptions.DataDirectory`, `IProjectStorageLocation` | `Hosting/ServerOptions.cs:8`; `Infrastructure/Storage/ProjectStorageLocation.cs:11-13` | S2, S3, logging | yes, single abstraction | keep `IProjectStorageLocation` as the only root source; move it behind the component boundary |
| S2-2 Single-writer | `IDataDirectoryLock`, `DataDirectoryInUseException` | `Infrastructure/Storage/IDataDirectoryLock.cs:12`; `DataDirectoryLock.cs:13-26` | S2 only | yes | becomes the locking discipline of `ai-core` alone; every other component must lose the right to lock the root |
| S2-3 Path naming | `IProjectStoragePaths`, `IChatStoragePaths`, `IChatRunStoragePaths`, plus inline `Path.Combine` in 6 repositories | `ProjectStoragePaths.cs:7-13`; `ChatStoragePaths.cs:7-13`; `ChatRunStoragePaths.cs:5-9`; `JsonResourceRepository.cs:78`; `JsonReviewRepository.cs:86`; `JsonMemoryRepository.cs:92-93`; `JsonProjectInstructionsRepository.cs:58`; `SkillCatalog.cs:106-111` | S2, S3 | partly — 3 providers and 6 direct constructions | one path provider per entity kind, no repository building its own path; the on-disk layout becomes an internal detail of `ai-core` |
| S2-4 Atomic write | `IAtomicFileWriter`, `IFileSystem.MoveAsync(overwrite)` | `docs/04-storage.md` (`doc-only:`), `ProjectStoragePaths.cs:13` | S2, S3 | shape exists, not one named abstraction | a single `IAtomicDocumentStore` used by every repository, including the two-file chat save |
| S2-5 Chat transcript in one document | `ChatDocumentSerializer`, `ChatDocument` | `ChatDocumentSerializer.cs:164-192` | S2, S1 (context reads the whole chat) | no | split the transcript from the summary at the component boundary; the API must offer "one message" and "one branch chain" without shipping the whole document |
| S2-6 Diffs embedded in chat messages | `ChatMessageDocument.WorkspaceChanges` | `ChatDocumentSerializer.cs:273-285,257-271` | S2, S3 | no | move diff bodies to `S3` (blobs) and keep only a reference plus counters in the chat document |
| S2-7 Streaming content in run document | `Document.StreamingContent` | `JsonChatRunRepository.cs:102` | S2, S1 | no | run state becomes an event stream in `ai-core`; the partial text must not be rewritten to disk per delta |
| S2-8 Revision guards | `ProjectSaveResult`, `ChatSaveResult`, `MemoryWriteResult`, `SettingsConflictException` | `ProjectSaveResult.cs`; `ChatSaveResult.cs`; `MemoryWriteResult.cs` (`Memory/IMemoryRepository.cs`); `SettingsConflictException.cs` | S2, S5 | partial — four different result shapes | one revision contract (`If-Match` + 409 + `currentRevision`) for every entity, per charter C3 |
| S2-9 Secrets | `IGlobalSecretStore`, `IUserDataProtector`, `IMasterKeyStore` | `Application/Settings/IGlobalSecretStore.cs:3`; `Infrastructure/Credentials/*` | S2, S1, S5 | yes | secrets must stay inside one component and be handed out only as one-shot handles (charter C2); the key store location is a separate decision |
| S2-10 Settings without revision | `IGlobalSettingsRepository`, `IGlobalSettingsService` | `Application/Settings/IGlobalSettingsRepository.cs`; `GlobalSettingsService.cs:52-67` | S2, S5, S1 | yes | give settings a `revision` like every other entity, or keep item-level compare and document it as deliberate |
| S2-11 Host-provided MCP servers injected on read | `JsonGlobalSettingsRepository.LoadAsync` | `JsonGlobalSettingsRepository.cs:23-32` | S2, S1 | no | this is process knowledge inside a repository; move it to the application layer before the repository crosses a network |
| S2-12 Storage-lifetime routing | `ChatRepositoryRouter`, `ChatRunRepositoryRouter`, `IChatKindPolicyRegistry` | `ChatRepositoryRouter.cs:49-50`; `ChatRunRepositoryRouter.cs:15,86-92` | S2, S4 | yes | a policy that decides "disk or memory" is application logic; it must not survive as repository routing inside `ai-core` |
| S2-13 Search over all chats | `IChatSearchService` | `Application/Chats/ChatSearchService.cs:8,135-152` | S2, S5 | yes | becomes the expensive cross-component call; needs a server-side paging contract and a documented cost model |
| S2-14 Run cleanup by file pattern | `DeleteExceptAsync`, `DeleteChatAsync` | `JsonChatRunRepository.cs:46-70` | S2, S5 | yes | cleanup must become an explicit operation with a retained-id set, not a directory scan |
| S2-15 Schedules live beside chats | `ChatScheduleStore` | `Application/Schedules/ChatScheduleStore.cs:116-118` | S2, S5 | no — the store is in `Application` and reaches `IChatStoragePaths` and `IFileSystem` | either a first-class entity in `ai-core` or an explicit note that it is derived state |
| S2-16 History checkpoints | `IHistoryCheckpointRepository` | `Infrastructure/Storage/JsonHistoryCheckpointRepository.cs:12-13` | S2, S1 | yes | it is the model's view of history, not history: it belongs with the model component's cache, not in the metadata store |
| S2-17 Root-relative id formats | `ProjectId.ToString()`, `ChatId` | `Domain/Projects/ProjectId.cs:19`; `Domain/Chats/ChatId.cs` | S2 | no — inconsistent | one canonical id rendering ("lowercase dashed UUIDv7", charter C1/C3) used by paths, JSON and API alike |
| S2-18 Absolute roots as grants | `DirectoryGrant.CanonicalRoot`, `DirectoryGrantSettings.CanonicalRoot` | `Domain/Projects/DirectoryGrant.cs:22-26,47`; `Contracts/Projects/DirectoryGrantSettings.cs:3-8`; `ProjectDocumentSerializer.cs:29` | S2, S3, S5, S1 | no — the grant *is* an OS path | `ai-core` owns the grant as a logical root; a node-local binding resolves it to a directory (decision `D1`) |
| S2-19 Absolute paths in history | `ChatResource.Path`, `FileChange.Path`, `ChatReview.Files[]`, `ReviewComment.Path`, `UndoFile.Path` | `ChatDocumentSerializer.cs:264,286`; `ChatReview.cs:5,17-20`; `WorkspaceUndoService.cs:196` | S2, S3, S1 | no | history keeps `{grantId, relativePath}` and `{blobId}`; the absolute string survives as a display-only legacy field (decision `D3`) |
| S2-20 Resource catalog identity | `JsonResourceRepository.GetOrCreateAsync` dedup on `Path` | `JsonResourceRepository.cs:13-14,29-30` | S2, S3 | no — identity is a path string | dedup on `{grantId, relativePath}` (or on the blob), so a move on disk stops creating a duplicate entry |
| S2-21 Review catalog duplicates diffs | `ChatReview.SourceChanges` | `ChatReview.cs:5-13`; `ReviewService.cs:54,16` | S2, S3 | no | review stores blob references plus the comment anchors, not a second copy of the diff text |
| S2-22 Proc-local blob tickets | `ResourceAssetService._tickets` | `ResourceAssetService.cs:24,116-125` | S2, S3 | no | a handed-out content URL must be minted by the owner of the bytes (`ai-resources`), not kept in a dictionary next to the metadata |
| S2-23 Two blob addressing schemes | asset id = hash(bytes + media type); undo id = hash(bytes) | `ResourceAssetService.cs:47-54,133-134` | S2, S3 | no — inconsistent | one content-addressing rule for every blob |

## 5. External dependencies and constraints

- **File system only.** All storage goes through `AI.Contracts.FileSystem.IFileSystem` and, for
  platform-dependent path rules, `IPath`; storage code performs no OS call of its own
  (`docs/04-storage.md`, `doc-only:`; corroborated by every repository above).
- **Deliberate direct platform calls** (documented exceptions): the single-instance lock
  (`DataDirectoryLock.cs:13-26`), file-change notification, POSIX file modes and process start-up
  paths (`docs/decisions/ADR-013-file-system-contract.md`, `doc-only:`).
- **UTF-8 without BOM; absent path answers with absence** rather than throwing
  (`docs/04-storage.md`, `doc-only:`; relied upon at `JsonProjectRepository.cs:20-21`).
- **`MoveAsync(overwrite: true)` must go through `File.Replace` on Windows**, because a plain rename
  is refused while a reader holds the destination open (`docs/04-storage.md`, `doc-only:`). Every
  repository's save path depends on this.
- **One process per data directory.** Enforced by the lock, not by a database. This is the single
  constraint that makes "three components on three machines" impossible in the current design.
- **Encryption of secrets is user-account-scoped** (`IUserDataProtector`, `IMasterKeyStore`,
  `Infrastructure/Credentials/`), i.e. secrets are readable only by the same OS user on the same
  machine — incompatible with a secret store reached over the network unless the key moves too.
- **Privacy:** the data directory holds conversation content, tool output, diffs and (protected)
  provider keys. Any split across machines changes the trust boundary of all of it.
- `assumption:` `%LOCALAPPDATA%/AI` remains the default root; `AI_CLIENT_DATA_DIRECTORY` and
  `--data-dir` remain the only overrides.

## 6. Risks when this seam becomes a separate component

1. **Two-file chat save.** Transcript and summary are moved separately; the ordering is the only
   consistency mechanism. Over a network, a partial failure between the two moves needs an explicit
   repair operation rather than the lazy `MigrateSummaryAsync` path.
2. **Whole-document read-modify-write.** Every update rewrites the entire chat document, including
   every diff and every message. Latinency and bandwidth grow with chat size; a chat with large
   embedded diffs is a multi-megabyte PUT.
3. **Loss of in-process atomicity.** Today the revision check and the rename happen under one
   `SemaphoreSlim` inside one process. Across processes this must become a compare-and-set in the
   store itself, or two `ai-core` instances will lose updates silently — exactly what
   `IDataDirectoryLock` refuses today.
4. **Run-state write amplification.** `StreamingContent` is persisted as it streams. A remote store
   turns every delta into a network round trip unless the design replaces it with an event append.
5. **Revision semantics are four shapes today** (`ProjectSaveResult`, `ChatSaveResult`,
   `MemoryWriteResult`, exceptions, and settings' item compare). A component API that exposes all
   four would carry the accident into the contract; the redesign must pick one.
6. **Directory scans as a query language.** Run cleanup, schedule scan and chat search all enumerate
   directories and parse files. On a remote or object-backed store these become full scans with
   per-object latency.
7. **`HostLifetimeChatRepository` is invisible state.** Some chats and their runs never reach the
   disk at all. A component boundary draws a line through this: it must be modelled explicitly or
   removed.
8. **Secrets tied to the OS account.** Moving metadata to another machine also moves the encrypted
   credential files, which are only decryptable by the original user account. This is a decision, not
   a bug — but it must be decided before the split, not during it.
9. **No garbage collection.** `docs/04-storage.md` states unused nodes are retained and no collector
   exists (`doc-only:`); in practice obsolete `.tmp` files and superseded summaries likewise linger.
   A shared remote store makes retained bytes a cost rather than a local inconvenience.
10. **Tests replace the file system with an in-memory fake.** The fakes model in-process semantics;
    they will not model a networked store's failure modes, so the new boundary needs its own
    fault-injection tests.
11. **The path migration can only run where the paths still resolve.** Chat, resource, review and undo
    documents all carry absolute paths (`P2`, `P4`, `P6`, `P7`, `P8` in §3.8). Once the data moves to
    another machine those strings name nothing there, so the rewrite must happen **before** the move,
    on the machine that owns the directories, and every format has to be done together — a half-migrated
    chat document breaks the review anchors that point into its diffs.
12. **Some references have no portable answer.** A path outside every grant
    (`WorkspacePathResolver.cs:52-54`), a client-side upload path (`ChatResource.cs:40`), and the file
    names inside `ToolCalls[].Arguments` cannot be expressed as `{grantId, relativePath}`. They need a
    stated fallback (display-only legacy string, or dropped with the message annotated) rather than a
    silent rewrite.
13. **Resource identity changes shape.** Dedup by absolute path
    (`JsonResourceRepository.cs:13-14,29-30`) means two grants containing the same relative path are one
    resource today and would become two. Downstream consumers that assume one `@file` entry per file
    must be checked before the key changes.

## 7. Input for the target design

1. **One owner of metadata.** Let `ai-core` own the data directory root, its lock and the entire
   on-disk layout; no other component resolves a path or locks a directory. Keep
   `IProjectStorageLocation`/`IPath` as the single root abstraction and delete every inline
   `Path.Combine(location.RootDirectory, ...)` from repositories before the split.
2. **One revision contract for every entity.** Replace `ProjectSaveResult`, `ChatSaveResult`,
   `MemoryWriteResult` and the settings item-compare with the charter's C3 shape: `If-Match` in,
   `409` plus `currentRevision` out, `428` when the header is absent. Settings documents must gain a
   document revision like the rest.
3. **Split the chat document at the boundary.** The component API must expose "append one message",
   "read one message", "read the branch chain" and "read chat headers as a page" without returning a
   whole transcript, so that a chat with large embedded diffs never crosses the wire as one object.
4. **Move diff bodies and undo snapshots out of metadata** into `S3`: the chat message keeps a blob
   reference, counters and the file path list, not the diff text.
5. **Turn run state into an append-only event log** owned by `ai-core`, with the current state derived
   (or cached with a TTL), rather than a rewritable document that embeds streaming text.
6. **Delete the storage-lifetime routing before the split.** `ChatRepositoryRouter`,
   `ChatRunRepositoryRouter` and `HostLifetimeChatRepository` encode application policy inside
   storage; keep the policy, but let one component answer every read and write.
7. **Make the expensive queries explicit operations** with documented cost: "search across chats"
   (cursor + budgets, as today), "cleanup runs except these branches", "list schedules of a project".
   These must be operations of the metadata API, not directory scans performed by the caller.
8. **Move the host-provided MCP server injection out of the repository** into the application layer,
   so that the metadata component stores exactly what it was given.
9. **Decide the secret strategy explicitly**: either secrets stay in `ai-core` behind one-shot
   handles (charter C2) or they move to a dedicated store with a machine-independent key; the current
   OS-account-bound protection cannot be split across machines unchanged.
10. **Correct `docs/04-storage.md` first.** It describes message-per-file node storage and schema 2
    for chats; the code stores one document per chat with schema 9. Every design statement built on
    that document would be wrong, so the current-state document and the design must both be based on
    the code references listed above.
11. **Grants are logical and belong to `ai-core`.** Store `{grantId, logicalRoot, recursive, toolNames}`
    in the project document and nothing else; a node-side binding resolves a grant id to a physical
    directory. Every reader of `CanonicalRoot` listed in §3.8 (`ProjectPathAccess`, `WorkspacePathResolver`,
    `ChatAgent`, `WorkspaceFileSearch`, the skills) must take its root from that resolver instead.
12. **Rewrite every stored path in one migration pass, before any data moves.** Chat document,
    resource catalog, review catalog and undo manifest become `{grantId, relativePath}` and `{blobId}`;
    the diff bodies leave metadata for `S3`; the previous absolute spelling survives as a display-only
    field so old messages still read sensibly. Publishers of the new format must keep reading the old
    one, exactly as the chat reader already spans schemas 5–9.
13. **Give the resource and review catalogs the same treatment as the other metadata.** One path
    provider, one gate per document rather than one per repository instance, a revision result instead
    of a thrown exception, and a review that references the blobs of its diff instead of copying them
    (`S2-19`–`S2-21`). The blob side keeps one addressing rule and one content-URL owner (`S2-22`, `S2-23`).
