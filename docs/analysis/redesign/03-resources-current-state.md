# S3 — Resources and bytes: current state

Seam `S3` of the charter (`docs/analysis/redesign/README.md` is the index; the charter lives in the
team's lead branch as the message "Team charter"). Scope: what bytes the monolith handles, where they
live, how they are addressed and which code reads and writes them. Facts are cited as `path:line`.
Everything not verified in code is marked `assumption:` or `external:`.

## 1. Scope

### In scope

- Directory grants and path rules that decide what a project may read or write
  (`DirectoryGrant` consumers: `ProjectPathAccess`, `WorkspacePathResolver`, `PhysicalDirectoryBrowser`,
  `ProjectStorageLocation`).
- Reading and searching files of the granted directories: listings, recursive walks, name search,
  line excerpts, whole-file reads.
- Working-tree change tracking and diffs: `WorkspaceChangeTracker`, `GitWorkspaceDiffReader`,
  `GitBrowser`, `WorkspaceUndoService`.
- Chat attachments and artifacts: `ResourceService`, `ResourceAssetService`, `JsonResourceRepository`,
  `ChatResource` and the resource kinds a message can carry.
- Reviews of saved changes and of chat messages: `ReviewService`, `JsonReviewRepository`,
  `ReviewCommentSuggestions`.
- File previews and preview formats, including the byte-serving endpoints.
- Chat temporary directories (`ChatTemporaryDirectory`) and every other place where bytes land on
  the file system by a path.
- The `IFileSystem` / `IPath` / `IAtomicFileWriter` abstraction itself, because it is the seam that
  decides how portable this area already is.

### Out of scope

- Project, chat, settings and other metadata documents (teammate Bo, `S2`), except where a metadata
  file happens to hold a path (see §3.7).
- Model calls and context assembly (teammate Ada, `S1`), except the one place where a resource is
  projected into a model request (§3.5).
- MCP tool implementations in `AI.Mcp.BuiltIn/Files/*` — they are the *clients* of the same byte
  layer and are covered only through the grant model they exercise.

## 2. Current structure

| Component | Role | Location |
| --- | --- | --- |
| `AI.Contracts.FileSystem` | The frozen byte/path abstraction: `IFileSystem`, `IPath`, `IAtomicFileWriter`, entries, options. Three implementations-ready surfaces, no OS assumption allowed in the contract. | `src/AI.Contracts/FileSystem/` |
| `AI.Contracts.Resources` | The wire shapes of the resource area: `ChatResource`, `ChatReview`, `FilePreview`, `ResourceSuggestion`, `ResolvePathsRequest`. | `src/AI.Contracts/Resources/` |
| `SystemFileSystem` / `SystemPath` | The platform adapter: real disk, real separators, real links. | `src/AI.Contracts/FileSystem/SystemFileSystem.cs`, `SystemPath.cs` |
| `Application/Resources` | Resource policy: path validation, resolution, preview, reviews, asset contract, model projection. | `src/AI.Server/Application/Resources/` |
| `Application/Workspace` | Working-tree change contracts (`IGitBrowser`, `ILineDiff`, `IWorkspaceUndoService`, `IWorkspaceChangeTracker`). | `src/AI.Server/Application/Workspace/` |
| `Infrastructure/Storage` (resource part) | Byte storage: asset blobs, resource catalog, review catalog, `ProjectStorageLocation`, `PhysicalDirectoryBrowser`. | `src/AI.Server/Infrastructure/Storage/` |
| `Infrastructure/Workspace` | Disk- and git-facing implementations of the above. | `src/AI.Server/Infrastructure/Workspace/` |
| `Infrastructure/Tools/ChatTemporaryDirectory.cs` | Per-chat scratch space under the OS temporary directory, granted to a run's tools. | `src/AI.Server/Infrastructure/Tools/ChatTemporaryDirectory.cs` |
| `Hosting/Endpoints` | The HTTP surface that hands bytes and paths to the UI. | `FilePreviewEndpoints.cs`, `FileSystemEndpoints.cs`, `GitEndpoints.cs`, `ChatEndpoints.cs` |

There is no separate resource repository project: the area is spread across `Application/Resources`,
`Infrastructure/Storage`, `Infrastructure/Workspace` and `AI.Contracts`, all inside the single
`AI.Server` process.

## 3. Data and formats

### 3.1 The byte kinds, end to end

This is the charter-mandated table: what bytes, where they live today, how they are addressed, and
what reads and writes them.

| Bytes | Where they live today | How they are addressed today | Read by | Written by |
| --- | --- | --- | --- | --- |
| Content of project files (in granted directories) | Wherever the grant points — outside the data directory entirely | Absolute OS path; a project sees it through link-resolved grants (`ProjectPathAccess.cs:22`, `WorkspacePathResolver.cs:52`) | `FileExcerptReader`, `WorkspaceFileSearch`, `FilePreviewService`, the MCP file tools | Only through tool sessions and Undo; the resource area itself is read-only here |
| Workspace file sent with its content | Data directory: `<data>/assets/<projectN>/<sha256>.bin` + `.json` sidecar | Content-addressed: asset id = SHA-256 of bytes + media type (`ResourceAssetService.cs:39-70`, directory at `:33`) | `ResourceModelProjection` via `ReadTextAsync` | `ResourceService.CaptureContentAsync` → `ResourceAssetService.StoreAsync` (`ResourceService.cs:248`) |
| Uploaded file / attached image | Same asset store, same addressing | Same `AssetId`; media type detected from the magic bytes (`ResourceAssetService.cs:44`) | `ChatEndpoints` ticket endpoint (`ChatEndpoints.cs:53`), model projection | `ResourceAssetService.StoreAsync` |
| Undo snapshots of files a run changed | `<data>/assets/<projectN>/undo/blobs/<sha256>.bin` and `<data>/assets/<projectN>/undo/<undoId>.json` manifests | Before/after pair addressed by SHA-256 of the bytes (`ResourceAssetService.cs:37`, `WorkspaceUndoService.cs:19,191`) | `WorkspaceUndoService.UndoAsync` | `WorkspaceChangeTracker.CaptureUndoAsync` → `StoreUndoBytesAsync` |
| Resource catalog (which paths are saved project resources) | `<data>/resources/<projectId>.json`, schema 1 | Path + kind, with a revision and a retired flag (`JsonResourceRepository.cs:78`) | `ResourceService.ValidateAsync` | `JsonResourceRepository` |
| Review of saved changes / of a chat message | `<data>/resources/reviews-<projectId>.json`, schema 1 | Review id (UUIDv7) plus revision; the chat id is checked on every access (`JsonReviewRepository.cs:86`) | `ResourceModelProjection`, `ReviewService` | `JsonReviewRepository` |
| Captured excerpt of a file's lines | Inside the chat message JSON (immutable node), not a separate blob | `ChatResource.Excerpt`, bounded to 1 000 lines / 48 KiB (`FileExcerptReader.cs:15`) | The model projection | `ResourceService.ReadExcerptAsync` (`ResourceService.cs:274`) |
| Captured `git diff` of a directory | Inside the chat message JSON | `ChatResource.Excerpt`, bounded to 96 KiB (`GitWorkspaceDiffReader.cs:14,92`) | The model projection | `ResourceService.CaptureDiffAsync` |
| Live file/directory contents for preview | Wherever the grant points; nothing is copied | `FilePreview` (metadata) + a random one-file ticket `file-preview-content/{key}` | `FilePreviewEndpoints.cs:15,18,23` | Nothing — preview is read-only |
| Chat scratch files produced by tools | OS temporary directory: `%TEMP%/AI.Client-<userkey>/<projectN>/<chatN>/` | Absolute path; granted to the run as a `ToolDirectoryGrant` | Any tool in the run | Any tool in the run (`ChatTemporaryDirectory.cs:17,19`) |
| Diff text of a saved change set | Inside the assistant message's `WorkspaceChangeSet` | Per-file `Diff` string in the metadata document | `ReviewService.ValidateComments`, `ReviewModelProjection` | `WorkspaceChangeTracker` + `ILineDiff` |
| C# tool scripts | Chat scratch or a script directory owned by the C# MCP server | Absolute path | `AI.Mcp.CSharp` | `AI.Mcp.CSharp` (not analysed here, listed for completeness) |

Two independent addressing schemes already coexist in this table: **path-addressed** (project files,
previews, scratch) and **content-addressed** (assets, undo blobs). Only the second one is ready for
an object store.

### 3.2 The abstraction that already exists

`IFileSystem` is a full, explicitly disk-free contract: every member is documented as answerable by
an in-memory implementation (`IFileSystem.cs:1-30`). Members relevant to a byte service:

- `ReadBytesAsync` (`IFileSystem.cs:66`), `OpenReadAsync` (`:93`), `ReadLinesAsync` (`:76`) — null on
  absence rather than throwing.
- `ListEntriesAsync` with enumeration options (`:159`, `:167`).
- `MoveAsync` (`:183`) — with the documented Windows "must go through `File.Replace`" requirement.
- `ResolveLinkTargetAsync` (`:227`) — described in its own remarks as a security primitive, because a
  junction inside a grant would otherwise look like a child of it.

`IPath` is pure string algebra with per-platform semantics chosen by the implementation
(`IPath.cs:1-30`); `IsInside` (`:64`) is the containment rule every grant check goes through
(`ProjectPathAccess.cs:36`, `SystemPath.cs:48`).

`IAtomicFileWriter` (`IAtomicFileWriter.cs:13`) is the crash-safe write path used for asset blobs and
manifests, not by chat history (which has its own sequence).

`docs/04-storage.md` and `docs/decisions/ADR-013-file-system-contract.md` describe this contract and
its deliberate platform exceptions; they are the project's own documentation, not independent
evidence.

### 3.3 Cloud / S3 blockers

This section is the charter's "what exactly prevents S3 and cloud storage today".

1. **Paths are absolute OS paths, everywhere.** `ChatResource.Path` is documented as an absolute path
   for file/directory/diff kinds (`ChatResource.cs:9-20`). A message stores
   `C:\Projects\...\app.cs`, so the same chat cannot be read from another machine: the identifier of
   the content is a location on one specific host. There is no relative-to-grant path, no project
   root id, no volume id in the stored resource.
2. **The resource area is in-process and synchronous by design.** Containment checks bridge a
   completed task with `GetAwaiter().GetResult()` (`ProjectPathAccess.cs:18`,
   `WorkspaceFileSearch.cs` walk, `ChatTemporaryDirectory.cs:IsLink`). This is safe only because the
   implementation never actually waits. A remote byte service has real latency, so every one of these
   call sites becomes a blocking network call or must be redesigned asynchronously.
3. **Existence and enumeration are assumed cheap.** `WorkspaceFileSearch` builds an in-memory index by
   walking the tree in the background (`WorkspaceFileSearch.cs:29,34,310`) and `ResourceService`
   counts changed files by running `git status` per repository in parallel
   (`ResourceService.cs` `ListDiffSourcesAsync`). Against S3 a "list everything" is a paginated
   network call with a per-request cost.
4. **Bytes and metadata live in one data directory.** `<data>/assets/...`, `<data>/resources/*.json`
   and `<data>/projects/...` share `ProjectStorageLocation.RootDirectory`
   (`ProjectStorageLocation.cs:12`) and are protected by a single process-wide data-directory lock
   (`.lock`, `DataDirectoryLock.cs:20`). Splitting bytes onto S3 means splitting one lock into two
   independent consistency domains, and the lock today forbids two processes on one directory at all.
5. **Content addressing is already there but only inside the asset store.** `ResourceAssetService`
   derives `assetId` from SHA-256 over bytes + media type and leaves an identical blob alone
   (`ResourceAssetService.cs:39-70`). That is exactly an object-store key — but the same addressing is
   *not* used for project files, previews or excerpts, and `AssetId` is scoped per project directory,
   so the same bytes uploaded to two projects are stored twice.
6. **No versioning and no immutability guarantee for blobs.** Blobs are written once and never
   overwritten (the writer checks existence first), which is good, but there is no version, no
   retention rule, no garbage collector: `docs/04-storage.md` states unused nodes are retained and no
   GC exists. Deletion is a recursive directory delete (`ResourceAssetService.cs:153`).
7. **Serving bytes relies on the local file system's own ranged serving.** The preview endpoint hands
   the path to `Results.File(..., enableRangeProcessing: true)` (`FilePreviewEndpoints.cs:23`), and the
   asset ticket endpoint similarly serves from disk. A remote store needs its own range/signing path.
8. **Presigned/ticket authorization is in-memory only.** Both the preview ticket table
   (`FilePreviewService.cs:33`, cap 4 096 entries) and the asset ticket table
   (`ResourceAssetService.cs:120`, 4-hour TTL) are `ConcurrentDictionary` fields of a singleton in one
   process. A second instance on another machine cannot honour a ticket the first one issued.
9. **A read-only window to the whole local machine is exposed.** `/api/filesystem/roots|directories|resolve`
   (`FileSystemEndpoints.cs:17,19,26,30,37`) browse any path on the host and are only bounded by
   `ServerOptions.BrowseEnabled` (`:21`) and loopback CORS. `docs` calls this out in the endpoint's own
   doc comment. Moving the resource area to a separate machine makes this window a network-reachable
   one unless it moves with the orchestration layer, not with the byte service.
10. **Git reads are process launches against a local checkout.** `GitBrowser.cs:49` and
    `GitWorkspaceDiffReader.cs:130` run `git -C <dir>`. On a remote byte service the diff source is not
    a local working tree at all, so "uncommitted changes of a granted directory" must either stay an
    orchestration-side capability or be replaced by a checkout service.
11. **Two Windows/Linux path semantics are baked into policy code, not only the adapter.**
    `ResourceService` picks `StringComparison` from `OperatingSystem.IsWindows()`,
    `WorkspaceChangeTracker` compares paths with `OrdinalIgnoreCase` unconditionally, and
    `WorkspaceUndoService.MayWrite` branches on the OS. These decisions must become properties of the
    byte service's addressing, not of the caller's OS.

### 3.4 Seams and coupling (`S3`)

| Seam | Type / interface | `file:line` | Areas affected | Seam exists today? | Proposal (`input for the target design`) |
| --- | --- | --- | --- | --- | --- |
| `S3.1` | `IFileSystem` (whole contract) | `AI.Contracts/FileSystem/IFileSystem.cs:1` | Byte service, metadata service, tools | Yes — the only real seam | Keep as the byte service's internal contract; put a remote implementation behind it |
| `S3.2` | `IPath` (semantics, `IsInside`) | `AI.Contracts/FileSystem/IPath.cs:64` | Grants, search, undo | Yes | Move to the byte service; callers stop branching on `OperatingSystem` |
| `S3.3` | `IAtomicFileWriter` | `AI.Contracts/FileSystem/IAtomicFileWriter.cs:13` | Assets, manifests | Yes | Byte-service primitive; must not be handed out across a process boundary |
| `S3.4` | `IProjectPathAccess.ResolveLinks/CanRead/AccessOf` | `Application/Resources/IProjectPathAccess.cs:23` | Grants, resources, preview, undo | Yes, but tied to `ProjectDetails` from the metadata service | Split: the *grant decision* belongs where grants live; *link resolution* belongs to the byte service |
| `S3.5` | `IResourceAssetService` | `Application/Resources/IResourceAssetService.cs:8` | Attachments, undo blobs, content tickets | Yes | Becomes the byte service's public API; `ReadAsync` returning `byte[]` must become a stream |
| `S3.6` | `IFilePreviewService` + ticket tables | `Application/Resources/IFilePreviewService.cs` | UI preview, byte serving | Yes | Preview stays orchestration-side; only byte reads move, and tickets must become stateless or signed |
| `S3.7` | `IResourceRepository` / `IReviewRepository` (JSON per project) | `Infrastructure/Storage/JsonResourceRepository.cs:78`, `JsonReviewRepository.cs:86` | Resource catalog, reviews | Yes, but they are *metadata* in `S3` naming | Move to `ai-core` (metadata), not to the byte service: they are small JSON documents |
| `S3.8` | `IWorkspaceDiffReader` / `IGitBrowser` | `Application/Resources/IWorkspaceDiffReader.cs` | Change capture, review, Git browsing | Yes, git is a process behind it | Keep the interface; the implementation must run where the checkout is, i.e. orchestration or a separate checkout component |
| `S3.9` | `IWorkspaceUndoService` | `Application/Workspace/IWorkspaceUndoService.cs` | Undo of a run's changes | Yes | Writes to granted directories *and* stores blobs: needs both `ai-resources` (blobs) and write access to the workspace |
| `S3.10` | `WorkspaceChangeSet` inside message metadata | `AI.Contracts/Workspace/WorkspaceChangeSet.cs:7` | Chat history, review, context | No — it is embedded in `S2` documents | Decide whether diffs are metadata (`ai-core`, small) or blobs (`ai-resources`, bounded 96 KiB) |
| `S3.11` | `ChatResource.Path` = absolute OS path | `AI.Contracts/Resources/ChatResource.cs:9` | Every stored reference | No — this is the central coupling | Replace with `{projectId, grantId, relativePath}` or `{blobId}`; keep the spelling only as a display field |
| `S3.12` | `ChatResource.AssetId` (SHA-256, project-scoped) | `ChatResource.cs` + `ResourceAssetService.cs:39` | Attachments, undo | Partly — content addressing exists | Promote to the global blob id of `C4`; drop the project scope from the key |
| `S3.13` | `ChatTemporaryDirectory` (absolute path under `%TEMP%`) | `Infrastructure/Tools/ChatTemporaryDirectory.cs:17` | Tool sandbox | No — local-only, OS temporary directory | Either orchestration-local scratch (documented as node-local) or a per-run short-lived bucket |
| `S3.14` | `/api/filesystem/*` host browse window | `Hosting/Endpoints/FileSystemEndpoints.cs:17` | UI directory picker, grants | No | Keep node-local and authenticated; never expose it from the byte service |
| `S3.15` | `Results.File(..., enableRangeProcessing: true)` on a local path | `Hosting/Endpoints/FilePreviewEndpoints.cs:23` | Preview/download | No | Byte service must serve ranges itself (or hand out a signed URL) |
| `S3.16` | `IDirectoryBrowser` + `PhysicalDirectoryBrowser.Canonicalize` | `Infrastructure/Storage/PhysicalDirectoryBrowser.cs:79` | Grant creation, path resolution | Yes | Stays node-local: a grant is made of a path on a specific machine |

### 3.5 Where bytes meet the model

One place in this area feeds the model: `ResourceModelProjection.ProjectAsync` renders each reference
as text, and for a file with an `AssetId` it inlines up to 64 KiB of decoded UTF-8 and says so
(`ResourceModelProjection.cs:52`). Read-only-ness is enforced by `ReadTextAsync`, which refuses binary
content (NUL byte or control characters) and truncates at 64 KiB
(`ResourceAssetService.cs:23,89-110`). This is the only consumer that requires *decoded text* rather
than a stream, and it is the reason `IResourceAssetService` currently returns `byte[]`.

### 3.6 Sizes and limits that the target design must keep

| Limit | Value | Source |
| --- | --- | --- |
| Attachment / captured content | 1 byte – 15 MiB | `ResourceAssetService.cs:22,44` |
| Text projected into the model | first 64 KiB | `ResourceAssetService.cs:23` |
| File excerpt in a message | 1 000 lines / 48 KiB | `FileExcerptReader.cs:15` |
| Captured git diff | 96 KiB, 200 untracked names | `GitWorkspaceDiffReader.cs:14` |
| Tracked file compared by content | 8 MiB | `WorkspaceChangeTracker.cs:33` |
| Undo snapshot per capture | 64 MiB total | `WorkspaceUndoService.cs:19` |
| Resource references per message | 20 unique | `ResourceService.cs` `ValidateForChatAsync` |
| Paths resolved per request | 100 | `WorkspacePathResolver.cs:10` |
| Path length | 4 096 chars | `ResourceService.cs:225,295` |
| Search index entries / walk dirs | 100 000 / 5 000 | `WorkspaceFileSearch.cs:34`, `GitWorkspaceDiffReader.cs:17` |
| Preview tickets / asset tickets | 4 096 entries / 4 h TTL | `FilePreviewService.cs:33`, `ResourceAssetService.cs:120` |
| Preview entries listed | 1 000 | `ArchiveFilePreviewFormat.cs:26`, `DirectoryFilePreviewFormat.cs:21` |

### 3.7 Metadata documents that the resource area owns anyway

Two documents are small JSON files that live in the same data directory but conceptually belong to
the byte area: the resource catalog `<data>/resources/<projectId>.json` (schema 1,
`JsonResourceRepository.cs:78`) and the review catalog `<data>/resources/reviews-<projectId>.json`
(schema 1, `JsonReviewRepository.cs:86`). Both use the `.tmp` + `MoveAsync(overwrite: true)` pattern
rather than `IAtomicFileWriter`. Both are per-project, which means a project's resources and reviews
must live in one metadata store to keep a single writer per file.

## 4. External dependencies and constraints

- **Git is an external process.** `GitBrowser.cs:49`, `GitWorkspaceDiffReader.cs:130`; the API tolerates
  its absence at runtime (returns null / empty), and `docs/10-operations.md` records the diagnostics line.
- **The OS file system is not abstracted away everywhere.** Documented exceptions in
  `ADR-013-file-system-contract.md` and `docs/04-storage.md`: process working directory, executable
  paths, POSIX modes, single-instance lock, file-change notification. In this area the visible ones are
  `ChatTemporaryDirectory` (`Path.GetTempPath()`, `File.SetUnixFileMode`, `Directory.CreateDirectory`
  with a mode at `ChatTemporaryDirectory.cs:17,60-90`) and `WorkspaceUndoService.MayWrite`
  (`WorkspaceUndoService.cs:150-165`) and `WorkspaceChangeTracker`'s `Path.GetFullPath` fallbacks.
- **`Microsoft.AspNetCore.StaticFiles`** (`FileExtensionContentTypeProvider`) decides MIME types from
  the file name in three places (`ResourceAssetService.cs:25`, `FilePreviewService.cs:21`,
  `FilePreviewTextReader.cs:12`), plus the endpoint itself (`FilePreviewEndpoints.cs:11`).
- **`System.Security.Cryptography`** for content addressing and undo verification
  (`ResourceAssetService.cs`, `WorkspaceUndoService.cs`, `FilePreviewService.cs` ticket randomness).
- **Confidentiality**: project file contents leave the machine only through model requests; the asset
  store is under the data directory and is not encrypted today. Secrets are `S2`'s area
  (`IGlobalSecretStore`), but the asset store should be checked against the same protection level.
- `external:` S3 / Azure Blob / any object-store semantics (ETag, multipart, eventual list
  consistency, presigned URL expiry) are **not** verified here — no such code exists in the repository.

## 5. Risks

1. **Absolute paths in stored chat history.** Any move of the byte layer to another machine breaks
   reading existing chats unless the path is re-based. Schema migration is required and cannot be
   additive (`ChatResource.Path` is already serialized).
2. **The synchronous seams.** `ResolveLinks`/`IsInside`/`ListEntries` bridged with
   `GetAwaiter().GetResult()` are called from synchronous policy code; a remote implementation turns
   them into blocking I/O on request threads (dead-lock and thread-pool starvation risk under load).
3. **Single data-directory lock.** Two components sharing one data directory is refused today
   (`DataDirectoryLock.cs:20-30`); splitting bytes to another host while metadata stays local means
   designing two independent lock/consistency domains and dropping the "one process" assumption.
4. **Tickets are process-local.** Load-balanced instances cannot validate each other's preview or
   asset tickets (`FilePreviewService.cs:33`, `ResourceAssetService.cs:120`).
5. **No garbage collection.** Blobs and undo snapshots accumulate; with an object store this becomes a
   storage cost rather than a disk-space annoyance. `docs/04-storage.md` states no GC exists.
6. **Grant revocation vs. cached previews/indexes.** `FilePreviewService.ResolveContentAsync` re-checks
   the grant and re-resolves links on every content request, which is the right behaviour; the search
   index (`WorkspaceFileSearch`, 30 s lifetime) and `WorkspaceChangeTracker` baselines hold paths that
   may no longer be granted. A remote byte service must not cache authorization decisions.
7. **The host browse window.** `/api/filesystem/*` is a full read-only view of the host for anyone who
   can reach the API; today only loopback origins. If the orchestration layer becomes remote, this
   window becomes the highest-risk endpoint in the area.
8. **Reviews reference a saved change set by path.** `ReviewService.ValidateFiles/ValidateComments`
   compare against `WorkspaceChangeSet` paths, so a path-addressing change in `S3.11` must migrate
   review comments too.
9. **Undo writes into grant directories.** `WorkspaceUndoService.UndoAsync` restores files in place
   with a temp-then-move sequence. If the byte service owns the blobs but not the write path into the
   workspace, undo needs a write capability on the byte service that no other operation has.
10. **Two path-comparison policies.** `OrdinalIgnoreCase` in `WorkspaceChangeTracker`/`WorkspaceUndoService`
    and case-sensitive comparison elsewhere can disagree on a case-sensitive file system, producing a
    "changed" file that is not, or a missed undo conflict.

## 6. Input for the target design

1. **Make `ai-resources` content-addressed, not path-addressed.** Its public key is
   `blobId = sha256(bytes)` (the existing `AssetId` scheme, globally scoped instead of per project),
   with `size`, `mediaType` and an optional `createdAt`. Paths never cross the component boundary.
2. **Follow `C4` of the charter**: `PUT /v1/blobs` with `x-content-sha256`, `GET /v1/blobs/{id}` with
   ranges, `POST /v1/blobs/{id}/materialize`. Never expose a local path or a local-file reader through
   this API; a caller that needs a file on its own disk asks for materialization and gets bytes.
3. **Split `S3.4` in two.** The *grant decision* ("may this project read this logical path") belongs to
   the component that owns projects (`ai-core`) and must be expressed over project-relative paths. The
   *link resolution and containment* over physical bytes belongs to `ai-resources` (or to the node that
   hosts the granted directory, if grants stay node-local — this is the one open architectural fork and
   should be raised with the lead).
4. **Introduce a workspace identity in stored resources.** Replace `ChatResource.Path` (absolute) with
   `{grantId, relativePath}` — or `{blobId}` for captured content — keeping the absolute spelling only
   as a non-authoritative display field. This is a schema change for `S2` as well, so it must be
   agreed with the metadata owner.
5. **Keep workspace-relative capabilities where the bytes are not.** Git diffs, directory browsing for
   grants, link resolution inside a granted directory and the temporary directory are *node-local*
   capabilities; they must not be folded into a cloud-capable byte service. Define them as an optional
   "local node agent" facet rather than as part of `C4`.
6. **Make every byte read a stream and every policy check an awaited call.** The `byte[]` returns of
   `IResourceAssetService.ReadAsync` and the bridged completed tasks of `ProjectPathAccess` are the two
   concrete blockers; a remote implementation is impossible without changing both signatures.
7. **Move the two small catalogs (`resources/<projectId>.json`, `reviews-<projectId>.json`) to the
   metadata component**, or state explicitly that they belong to the byte component — right now they
   sit in neither and share the file system with byte blobs under one lock.
8. **Decide the consistency contract for blobs**: bytes are immutable once written, deletion is
   explicit, tickets are stateless (signed, or resolved by the component that issued them), and no
   authorization decision is cached. Add the missing garbage collection for orphaned blobs as part of
   the design, because an object store makes leaks permanent.

## 7. Verification

- Every claim above is anchored to a `file:line` obtained by reading the file in this repository at
  commit `7bb4d34` (branch `master`), not to a project document. Project documents were consulted as
  context only and are named as such (`docs/04-storage.md`, `docs/24-chat-artifacts.md`,
  `docs/analysis/filesystem-abstraction-audit.md`, `docs/decisions/ADR-013-file-system-contract.md`).
- `external:` S3 and other object-store facts are assumptions, not verified behaviour of this codebase.
- Not covered here and left to the lead's judgement: the exact schema migration shape for
  `ChatResource.Path` (overlaps `S2`), and whether grants remain node-local (overlaps `S5`).
