# File-system abstraction audit (phase 0)

Owner: Ada · Analyst. Scope: audit only, no production code. This document is the input for the
migration order (Cleo · Backend, Dan · Backend) and for test modularization (Eva · QA).

Method: every number below was produced with read-only commands against the charter's base commit
`ca78bd57c0ac9851296e23989bdbdba37e78b0b7`, not against the working tree, because phase 1 and phase 2
work is already on disk next to this audit (`git grep -n -E … <base> -- 'src/**/*.cs'`). Line numbers
are therefore stable for the whole migration; a line number that has moved by the time a teammate
opens the file has simply been edited by someone else.

Categories used throughout:

- **OS-IO** — a call that touches the host file system: `File.*`, `Directory.*`, `FileInfo`,
  `DirectoryInfo`, `DirectoryInfo`/`FileSystemInfo` enumeration, `FileStream`, `FileSystemWatcher`.
- **Path** — `System.IO.Path` string algebra. Not IO (charter glossary).
- **Exception** — a documented exception from the charter (list reproduced in §7), counted but not
  migrated.

Legend for target members (`IFileSystem` unless prefixed):

| Short | Member |
| --- | --- |
| `FE` / `DE` | `FileExistsAsync` / `DirectoryExistsAsync` |
| `GE` / `GA` / `GLW` | `GetEntryAsync` / `GetAttributesAsync` / `GetLastWriteTimeAsync` |
| `RT` / `RB` / `RL` / `OR` | `ReadTextAsync` / `ReadBytesAsync` / `ReadLinesAsync` / `OpenReadAsync` |
| `WT` / `WB` / `AT` / `OW` | `WriteTextAsync` / `WriteBytesAsync` / `AppendTextAsync` / `OpenWriteAsync` |
| `CDD` | `CreateDirectoryAsync(path, ownerOnly, ct)` |
| `LF` / `LFR` / `LE` | `ListFilesAsync` / `ListFilesRecursivelyAsync` / `ListEntriesAsync` |
| `MV` / `CP` / `DF` / `DD` | `MoveAsync` / `CopyAsync` / `DeleteFileAsync` / `DeleteDirectoryAsync` |
| `RLT` | `ResolveLinkTargetAsync` |
| `AFW` | `IAtomicFileWriter.WriteTextAsync` / `WriteBytesAsync` |
| `IPath.*` | `IPath` members (`Comb` = `Combine`, `Full` = `GetFullPath`, `DirName` = `GetDirectoryName`, `Root` = `GetPathRoot`, `FName` = `GetFileName`, `FNameNoExt` = `GetFileNameWithoutExtension`, `Ext` = `GetExtension`, `Rel` = `GetRelativePath`, `II` = `IsInside`, `FQ` = `IsFullyQualified`, `Rt` = `IsRooted`, `EIDS` = `EndsInDirectorySeparator`, `TES` = `TrimEndingDirectorySeparator`, `Cmp` = `Comparison`, `DS`/`ADS`/`PS`/`IPC` = separators / `InvalidPathChars`, `CaseSensitive`) |

## 1. Inventory: measured counts and reconciliation with the charter

| Measure | Charter | This audit (base `ca78bd57`) |
| --- | --- | --- |
| Lines matching `File.*` / `Directory.*` method calls under `src/**/*.cs` | 176 | **196** lines, **54** files |
| Lines constructing `FileInfo` / `DirectoryInfo` / `FileStream` / using `FileSystemInfo` | not counted separately | **41** lines, 24 files |
| Total direct OS operations | 176 | **237** operations across **56** distinct files |

Reconciliation. The pattern matters: the charter's AC1 pattern
(`\bFile\.(Exists|ReadAll|WriteAll|Delete|Move|Copy|Replace|Open|Create|AppendAll|GetLastWrite|SetUnixFileMode)`
and `\bDirectory\.(Exists|Create|Delete|Move|GetFiles|Enumerate|SetCurrent|GetCurrent)`) matches 196
lines here, and my per-file listing (§3–§4) reproduces that set line by line. The difference of 20 is
explained by the two classes of site the charter's "176 direct calls" description did not separate:

1. **16 lines inside `src/AI.Server/Infrastructure/Storage/PhysicalTextFileSystem.cs`** — the existing
   adapter. Charter AC1 explicitly excludes adapters (`SystemFileSystem`/`SystemPath`), and this file's
   replacement is one of them; 196 − 16 = 180.
2. **4 lines that are not `File.`/`Directory.` *calls* but direct type uses on the same logical
   operation** — `IFilePreviewFormat.cs:10` (`new FileInfo(Path).Length`), `PathGuard.cs:113`,
   `ProjectPathAccess.cs:21` (`new DirectoryInfo`/`new FileInfo`), `DirectoryTreeTool.cs:73`
   (`new DirectoryInfo(...).GetFileSystemInfos()`). 180 − 4 = 176.

So the charter's 176 is the count of **call sites outside adapters using the method form**; the true
migration surface is **196 call-form sites + 41 constructor/enumeration sites = 237 operations in 56
files**. Nothing in the migration set is lost by the change of number: every site the charter counted
appears below, and the constructor sites (which carry `Length`, `Attributes`, directory enumeration and
the `PathGuard`/`ProjectPathAccess` link walk) are precisely the ones a naive AC1 grep would miss — Eva
should treat them as a first-class list, not as false positives (§8).

Files with calls but **no** direct OS operation: `src/AI.Web/**` (0 hits) — the web client performs no IO
of its own, so Dan's web share is a verification task, not a migration task (see §5.4).

## 2. Path-operation inventory (`System.IO.Path`)

`System.IO.Path` appears in 65+110 = 175 lines under `src`. Split by the charter's rule — pure string
algebra stays, anything carrying platform comparison, containment, canonicalization or link semantics
moves to `IPath`.

### 2.1 Stays as `System.IO.Path` (pure string algebra, no platform semantics needed)

| Site | Calls |
| --- | --- |
| `src/AI.Desktop/JsonClientSettingsStore.cs:8`, `JsonThemePreferenceStore.cs:7`, `JsonWindowPlacementStore.cs:9`, `JsonWorkspaceLocationStore.cs:8`, `MainWindow.axaml.cs:180`, `WindowsTaskbarBadge.cs:46` | `Combine`, literal sub-segments |
| `src/AI.Server/Infrastructure/Storage/ChatStoragePaths.cs`, `ChatRunStoragePaths.cs`, `ProjectStoragePaths.cs`, `JsonMemoryRepository.cs:92`, `JsonProjectInstructionsRepository.cs:58`, `JsonResourceRepository.cs:78`, `JsonReviewRepository.cs:86`, `GlobalSettingsPaths.cs:7`, `ChatRunStoragePaths.cs:5`, `DataDirectoryLock.cs:14`, `JsonLineFileLoggerProvider.cs:25` | `Combine` of storage layout, one bound directory |
| `src/AI.Server/Infrastructure/Storage/JsonLinesTokenUsageLedger.cs:51` | `FNameNoExt` on a file it listed itself |
| `src/AI.Server/Hosting/Endpoints/FilePreviewEndpoints.cs:35`, `src/AI.Server/Application/Resources/IFilePreviewFormat.cs:9` | `FName` for a display/download name |
| `src/AI.Server/Application/Resources/MarkupFilePreviewFormat.cs:10`, `ArchiveFilePreviewFormat.cs:11` (`Ext`) | `Ext` to pick a preview format |
| `src/AI.Mcp.BuiltIn/Archives/ZipCreateTool.cs:146`, `ZipExtractTool.cs:99`, `ZipListTool.cs:84` | `FName` / `TrimEnd(DS, ADS)` for archive entry labels |
| `src/AI.Mcp.CSharp/Scripts/ScriptRunner.cs:43,230` | `FNameNoExt`, `PathSeparator` split of `PATH` |
| `src/AI.Server/Updates/UpdateInstaller.cs:12` | `TrimEnd(DS)` on `AppContext.BaseDirectory` (exception 3 site) |

These keep `System.IO.Path`; the adapter `SystemPath` exists so that the *comparable* subset below has a
fake, not to ban string algebra.

### 2.2 Moves to `IPath` (comparison, containment, canonicalization, root semantics)

| Site | Call | `IPath` member | Why |
| --- | --- | --- | --- |
| `src/AI.Server/Application/Resources/ProjectPathAccess.cs:13,14,17,20,49` | `Full`, `Root`, split on `DS`/`ADS`, `Comb`, `EIDS` | `Full`, `Root`, `DS`/`ADS`, `Comb`, `EIDS` | containment walk used for path grants |
| `src/AI.Server/Application/Resources/ResourceService.cs:67,89,233` | `Rel`, `FName`, `TrimEnd(DS, ADS)` | `Rel`, `FName`, `TES` | grant-relative names; separator normalization is platform logic |
| `src/AI.Server/Application/Resources/WorkspacePathResolver.cs:42,74` | `FQ`, `IPC` | `FQ`, `IPC` | "is this an absolute path" decides resolution |
| `src/AI.Server/Infrastructure/Storage/PhysicalDirectoryBrowser.cs:67,83,90,91,93,167` | `FQ`, `Full`, `TrimEnd`, `Root`, `DirName` | `FQ`, `Full`, `TES`, `Root`, `DirName` | canonical browser root |
| `src/AI.Server/Infrastructure/Workspace/WorkspaceChangeTracker.cs:307,308,318,321` | `FQ`, `Full`, `EndsWith(DS)` prefix | `FQ`, `Full`, `EIDS`/`II` | containment of tracked files (`II` replaces the hand-built prefix) |
| `src/AI.Server/Infrastructure/Workspace/WorkspaceFileSearch.cs:127,145,167,224,228` | `Full`, `Rel`, `Comb`, `EIDS` | `Full`, `Rel`, `Comb`, `II` | search containment; `II` replaces `StartsWith(prefix, Cmp)` |
| `src/AI.Server/Infrastructure/Workspace/WorkspaceUndoService.cs:151,152` | `FQ`, `Full` | `FQ`, `Full` | refuses relative undo paths |
| `src/AI.Server/Infrastructure/Workspace/GitBrowser.cs:42`, `GitWorkspaceDiffReader.cs:31` | `FQ`, `Full` | `FQ`, `Full` | repository root |
| `src/AI.Server/Infrastructure/Tools/McpToolSession.cs:218,220`, `ExternalToolSessionFactory.cs:29,34,36` | `FQ`, `Rt`, `Full`, `Comb` | `FQ`, `Rt`, `Full`, `Comb` | working directory / command resolution |
| `src/AI.Server/Infrastructure/Tools/App/AppAskUserTool.cs:325` | `FQ` | `FQ` | git-picker repository path |
| `src/AI.Server/Infrastructure/Storage/ResourceAssetService.cs:163,164,165` | `Full`, `Comb`, `StartsWith(root + DS, OrdinalIgnoreCase)` | `Full`, `Comb`, `II` | containment of asset paths — the `OrdinalIgnoreCase` literal is exactly what `II`/`Cmp` centralize |
| `src/AI.Server/Infrastructure/Storage/ProjectStorageLocation.cs:12` | `Full` | `Full` | canonical data root |
| `src/AI.Server/Infrastructure/Tools/ChatTemporaryDirectory.cs:13` | `Comb(Path.GetTempPath(), …)` | `Comb` (+ `GetTempPath` stays, exception 3) | path of the scratch root |
| `src/AI.Desktop/SharedHostLocator.cs:13` | `Full` × 2, `StringComparison` | `Full`, `Cmp` | "same data directory" comparison |
| `src/AI.Mcp.BuiltIn/Grants/PathGuard.cs:23,29,49,57,85,92,101,108,127` | `FQ`, `Full`, `DirName`, `Root`, split on `DS`/`ADS`, `EndsWith` prefix | `FQ`, `Full`, `DirName`, `Root`, `DS`/`ADS`, `II` | the containment engine behind grants; `II(…, recursive)` replaces the hand-built prefix, `Cmp` replaces its private `Comparison` field |
| `src/AI.Mcp.BuiltIn/Archives/ArchivePaths.cs:38,54,55` | `FQ`, `Rt`, `Full`, `DirName?`/`DS` prefix | `FQ`, `Rt`, `Full`, `DS` | untrusted archive names re-based under the destination (`TryResolve`) |
| `src/AI.Mcp.BuiltIn/Process/ProcessRunner.cs:51,60,61,67` | `Full`, `FQ`, `DS`/`ADS` | `Full`, `FQ`, `DS`/`ADS` | executable/working-directory resolution |
| `src/AI.Mcp.BuiltIn/Triggers/TriggerWaiter.cs:99,116` | `DirName`, `TrimEnd(DS) + DS` prefix | `DirName`, `II` | watcher directory and contained-path test |
| `src/AI.Mcp.CSharp/Scripts/ScriptRunner.cs:199,357`, `ScriptRunTool.cs:53` | `Full`, `FQ` | `Full`, `FQ` | reference and working-directory resolution |
| `src/AI.Mcp.BuiltIn/Files/DirectoryTreeTool.cs:95`, `GrepFilesTool.cs:166`, `SearchFilesTool.cs:101` | `Rel` | `Rel` | displayed relative paths |
| `src/AI.Mcp.BuiltIn/Archives/ZipCreateTool.cs:158` | `Rel` | `Rel` | archive-internal names |

Rule for both developers: after migration no file under `src` may compute containment with
`StartsWith(x + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)` or with a private
`Comparison` constant; that expression is `IPath.IsInside` with `IPath.Comparison`.

## 3. Ordered migration list — `AI.Server/**` (owner Cleo · Backend)

Order keeps every step buildable. Steps 1–2 are the 13 former `ITextFileSystem` consumers: they already
depend on an injected abstraction, so they change namespace/type and gain the two renamed members only —
zero behavioural risk, and they are what makes the rest of the file set comparable.

### Step 1 — consumer rewiring, no logic change (13 types)

| # | File | Type | Note / risk |
| --- | --- | --- | --- |
| 1 | `src/AI.Server/Infrastructure/Settings/JsonGlobalSettingsRepository.cs` | `JsonGlobalSettingsRepository` | `ITextFileSystem` → `IFileSystem`, `ExistsAsync`→`FE`, `DeleteAsync`→`DF`. Risk: none observed |
| 2 | `src/AI.Server/Infrastructure/Settings/ProtectedGlobalSecretStore.cs` | `ProtectedGlobalSecretStore` | same; covered by `ProtectedGlobalSecretStoreTests` |
| 3 | `src/AI.Server/Infrastructure/Storage/JsonChatRepository.cs` | `JsonChatRepository` | same; `JsonChatRepositoryTests` already uses `MemoryFileSystem` |
| 4 | `src/AI.Server/Infrastructure/Storage/JsonChatRunRepository.cs` | `JsonChatRunRepository` | same |
| 5 | `src/AI.Server/Infrastructure/Storage/JsonHistoryCheckpointRepository.cs` | `JsonHistoryCheckpointRepository` | same |
| 6 | `src/AI.Server/Infrastructure/Storage/JsonLinesTokenUsageLedger.cs` | `JsonLinesTokenUsageLedger` | same; keeps `Path.FNameNoExt` (§2.1) |
| 7 | `src/AI.Server/Infrastructure/Storage/JsonMemoryRepository.cs` | `JsonMemoryRepository` | same |
| 8 | `src/AI.Server/Infrastructure/Storage/JsonProjectInstructionsRepository.cs` | `JsonProjectInstructionsRepository` | same |
| 9 | `src/AI.Server/Infrastructure/Storage/JsonProjectRepository.cs` | `JsonProjectRepository` | same |
| 10 | `src/AI.Server/Infrastructure/Storage/JsonResourceRepository.cs` | `JsonResourceRepository` | same |
| 11 | `src/AI.Server/Infrastructure/Storage/JsonReviewRepository.cs` | `JsonReviewRepository` | same |
| 12 | `src/AI.Server/Infrastructure/Storage/SkillCatalog.cs` | `SkillCatalog` | same; also does `Path.GetFileName(Path.GetDirectoryName(path))` at line 96 — switch to `IPath` |
| 13 | `src/AI.Server/Infrastructure/Workspace/WorkspaceUndoService.cs` | `WorkspaceUndoService` | **do not treat as a pure rename**: it also holds 8 direct calls (step 3) |

Then delete `src/AI.Server/Infrastructure/Storage/ITextFileSystem.cs` and `PhysicalTextFileSystem.cs`
and update `src/AI.Server/Composition.cs` (~line 104). Risk: `File.Replace`/retry semantics of
`PhysicalTextFileSystem.MoveAsync` must survive in the new `SystemFileSystem` (Bo's file) — the
`WorkspaceUndoService` and every JSON repository rely on replace-under-reader; verify with the AC4 run.

### Step 2 — DI and composition

| File | Change | Risk |
| --- | --- | --- |
| `src/AI.Server/Composition.cs` (~104) | bind `IFileSystem`, `IPath`, `IAtomicFileWriter` instead of `ITextFileSystem`/`PhysicalTextFileSystem` | DIW warnings (AC6); Pure.DI conventions per `docs/30-dependency-injection.md` |
| `src/AI.Server/Infrastructure/Tools/DefaultToolSessionFactory.cs:23`, `CSharpToolSessionFactory.cs:27,29,36` | MCP executable lookup via `IFileSystem.FE` + `IPath.Comb` | `AppContext.BaseDirectory` stays (exception 3); published layout must not change |

### Step 3 — `AI.Server` direct-call migration, in this order

| # | File | Type | Lines | Target | Risk note |
| --- | --- | --- | --- | --- | --- |
| 1 | `Infrastructure/Storage/ResourceAssetService.cs` | `ResourceAssetService` | 38,40,45,46,47,49,52,57,58,59,61,76,77,79,88,134,136,140,141,142,144,154,155,167 (24 lines, incl. `new FileStream` 89) | `CDD`,`FE`,`WB`,`MV`,`DF`,`RT`,`RB`,`OR`,`DD` + `AFW` for the temp-then-move saves; `PathGuard`-style target check via `IPath.II` (163–165) | heaviest site (22 in the charter's count). The temp-file→`File.Move`→swallow-`IOException`-if-destination-exists idiom is a *lost race*, not an error: keep it, or replace with `AFW` whose contract already says "atomically replace". `Directory.Delete(target, recursive: true)` at 167 is a cache wipe |
| 2 | `Updates/UpdateManager.cs` | `UpdateManager` | 42,46,53,56,71,156,170,182,190,195,219,222,231,247,248,249 (16 lines, + `FileStream` 72,187) | `FE`,`RT`,`CDD`,`MV`,`OR`,`DF`,`WT`,`GLW` | `.partial` download and `.tmp` state writes are the same temp-then-`Move` pattern; `owner.lock` at 72 is an **exclusive lock** — see the gap question in §7.2 |
| 3 | `Hosting/BrowserAccessService.cs` | `BrowserAccessService` | 81,83,84,95,96 | `CDD`,`AT`/`AFW`,`FE`,`RL` | hash-list file; `File.ReadAllLines`→`RL` (returns `IAsyncEnumerable<string>`; the `Where` filter moves to the caller) |
| 4 | `Infrastructure/Workspace/WorkspaceUndoService.cs` | `WorkspaceUndoService` | 90,94,95,97,99,130,137 (+ `new FileInfo` 135) | `CDD`,`WB`,`MV`,`FE`,`DF`,`RB`,`DE`,`GE` | the undo path must stay byte-faithful; `File.Exists(x) != AfterExists` at 130 is an "is it still what we recorded" check → `FE`/`DE` + `GE` |
| 5 | `Infrastructure/Workspace/WorkspaceChangeTracker.cs` | `WorkspaceChangeTracker` | 279,283,284 | `GE` (Length/attributes), `RB`, `RT` | snapshots are hashed; any change from `File.ReadAllBytes`/`ReadAllText` decoding must be byte-exact (UTF-8 no BOM) |
| 6 | `Infrastructure/Workspace/WorkspaceFileSearch.cs` | `WorkspaceFileSearch` | 93,129 (+ `FileSystemInfo` 131,132,289,290) | `DE`,`LE`/`LF`/`LFR` | replaces `DirectoryInfo.EnumerateFileSystemInfos` with `ListEntriesAsync`; `ListingOptions` (`AttributesToSkip`, `IgnoreInaccessible`) has **no contract member** — see §7.2 |
| 7 | `Infrastructure/Workspace/WorkspaceInstructionFileReader.cs` | `WorkspaceInstructionFileReader` | 26 (+ `new FileStream` 29) | `FE`,`RT` or `OR` | opens with `FileShare.ReadWrite \| FileShare.Delete` — no share mode in the contract (§7.2) |
| 8 | `Infrastructure/Workspace/FileExcerptReader.cs` | `FileExcerptReader` | 19,40 (`FileStream` with `FileShare.ReadWrite\|Delete`) | `OR` / `RB` | same share-mode gap; also `Path.GetFileName(path)` in the error message (§2.1) |
| 9 | `Infrastructure/Workspace/GitWorkspaceDiffReader.cs` | `GitWorkspaceDiffReader` | 44 (`new DirectoryInfo(...).EnumerateDirectories`), 50,51 | `LE`,`FE`,`DE` | directory enumeration for `.git` markers; `EnumerateDirectories("*", options)` maps to `LE` + `IsDirectory` filter |
| 10 | `Infrastructure/Logging/JsonLineFileLoggerProvider.cs` | `JsonLineFileLoggerProvider` | 27,57,78,80,82 | `CDD`,`AT`,`LF`,`GLW`,`DF` | append + rotation by last-write time; `AT` (contract) appends with UTF-8 no BOM, current code uses `File.AppendAllText` (platform default encoding) — verify no behaviour change for non-ASCII log lines |
| 11 | `Infrastructure/Credentials/FileMasterKeyStore.cs` | `FileMasterKeyStore` | 17,23,28,48 (+ `new FileStream` 43) | `FE`,`RT`,`CDD`,`OW`/`AFW`,`MV` | **lines 28,31,40,43 are exception 5** (POSIX modes); keep them, but route the existence/read/write/move through the contract. `File.Move(..., overwrite:false)` at 48 is the "never clobber an existing key" guarantee |
| 12 | `Application/Resources/FilePreviewService.cs` | `FilePreviewService` | 23,38 | `DE`,`FE` | returns `null` for absent/unreadable; contract returns `null`/`false` instead of throwing — behaviour preserved |
| 13 | `Application/Resources/FilePreviewTextReader.cs` | `FilePreviewTextReader` | 15,40 (`File.OpenText`) | `RL` or `OR` | line-oriented preview; `File.OpenText` uses BOM detection — the contract fixes UTF-8 no BOM, confirm the preview does not depend on BOM sniffing |
| 14 | `Application/Resources/IFilePreviewFormat.cs` | `IFilePreviewFormat` | 10 (`Directory.Exists`, `new FileInfo(Path).Length`) | `DE`,`GE` | record helper building `FilePreview`; `Length` → `GE.Length`, 0 for directories |
| 15 | `Application/Resources/DirectoryFilePreviewFormat.cs` | `DirectoryFilePreviewFormat` | 9,11,17 | `DE`,`LE` | replaces `EnumerateFileSystemEntries` with `LE(recursive:false)` |
| 16 | `Application/Resources/MarkupFilePreviewFormat.cs` | `MarkupFilePreviewFormat` | 9 | `DE` | one guard |
| 17 | `Application/Resources/MediaFilePreviewFormat.cs` | `MediaFilePreviewFormat` | 9 | `DE` | one guard |
| 18 | `Application/Resources/ArchiveFilePreviewFormat.cs` | `ArchiveFilePreviewFormat` | 11 | `DE` | keeps `ZipFile.OpenRead` (not OS IO in the charter's sense, see §7.2) |
| 19 | `Application/Resources/ResourceService.cs` | `ResourceService` | 62 | `DE` | plus `IPath` per §2.2 |
| 20 | `Application/Resources/ProjectPathAccess.cs` | `ProjectPathAccess` | 21 (+ `new DirectoryInfo`/`new FileInfo`, walk at 24) | `DE`/`FE` + `RLT` | **link walk**: needs synchronous reparse detection — §7.2 |
| 21 | `Infrastructure/Storage/PhysicalDirectoryBrowser.cs` | `PhysicalDirectoryBrowser` | 27,68,69 (+ `new DirectoryInfo` 35, `FileSystemInfo` 176) | `DE`,`FE`,`LE`,`GA` | platform listing (hidden/system, home expansion); decides what the picker shows — check on Windows and Linux |
| 22 | `Infrastructure/Storage/DataDirectoryLock.cs` | `DataDirectoryLock` | 13 (+ `new FileStream` 17 with `FileShare.None`) | `CDD` + **exclusive lock** | §7.2 gap: the contract has no lock member. Do not weaken single-instance protection |
| 23 | `Infrastructure/Tools/ChatTemporaryDirectory.cs` | `ChatTemporaryDirectory` (+ nested `DeleteTree`) | 51,54,55,59,66,69 (+ `new DirectoryInfo` 51,57,67,69,81) | `DE`,`CDD(ownerOnly:true)`,`LE`,`DF`,`DD` | lines 59 (+51,57,67 reparse checks) are **exception 5 / §7.2 link gap**; the recursive delete with reparse-point refusal is a security invariant — `DeleteDirectoryAsync(recursive:true)` must not follow links |
| 24 | `Hosting/InstalledDesktop.cs` | `InstalledDesktop` | 11,14,16 | `FE`,`DE` | exception-3 style platform probe at start-up; migrate the calls, keep `Environment.GetFolderPath` |
| 25 | `Updates/UpdateInstaller.cs` | `UpdateInstaller` | 15,16,22,29 | `DE`,`OW`/`AFW`,`WT` | writes a `powershell.exe` script; `AppContext.BaseDirectory` at 12 stays (exception 3) |
| 26 | `Updates/UpdateInstallationProvider.cs` | `UpdateInstallationProvider` | 14 | `FE` | `Path.Combine(AppContext.BaseDirectory, …)` stays |
| 27 | `Infrastructure/Tools/ExternalToolSessionFactory.cs` | `ExternalToolSessionFactory` | 29,35 | `DE`,`FE` | executable resolution; keep `IPath.FQ/Rt` |
| 28 | `CommandLine/ServerCommandLine.cs` | `ServerCommandLine` | 11 (`new DirectoryInfo(...)`) | validation only | `DirectoryInfo` is used to validate the `--data-dir` argument; replace with `IPath`/`IFileSystem` check or `Path.GetFullPath` on the parsed value. Low risk |

Every `AI.Server` step ends with `dotnet run --project build -- test` (AC4). Steps 1–2 are the ones the
13 consumers depend on; if the build is red between step 1 and Bo's adapters, that is the expected
charter situation — do not fix it by editing another owner's file.

## 4. Ordered migration list — MCP / Desktop / Web (owner Dan · Backend)

`ProjectReference` to `AI.Contracts` must be added to `AI.Mcp.BuiltIn` and `AI.Mcp.CSharp`
(`AI.Desktop` already references it). Per `build/McpBuiltIn.targets` and `build/McpCSharp.targets` the
MCP servers are separate executables copied to `<app>/mcp` and `<app>/mcp-csharp` — check the published
layout after each step.

### 4.1 Shared plumbing first

| # | File | Change | Risk |
| --- | --- | --- | --- |
| 1 | `src/AI.Mcp.BuiltIn/Grants/PathGuard.cs` | containment engine → `IPath` (`II`, `Cmp`, `DS`/`ADS`, `Root`) and the walk at 113–127 → `DE`/`FE`/`RLT` | **highest-value file in Dan's zone**: every tool's safety depends on it. Its private `Comparison` field must become `IPath.Comparison`; the synchronous link walk is the §7.2 gap |
| 2 | `src/AI.Mcp.BuiltIn/Files/*.cs` | the 17 file tools | mechanical once `PathGuard` is done |
| 3 | `src/AI.Mcp.BuiltIn/Archives/*.cs` | `ArchivePaths.TryResolve` → `IPath`, then the four tools | zip entry names are untrusted: containment must not weaken |

### 4.2 Per-file order

| # | File | Type | Lines | Target | Risk note |
| --- | --- | --- | --- | --- | --- |
| 1 | `Files/WriteFileTool.cs` | `WriteFileTool` | 42,47,50 | `DE`,`FE`,`WT` | creates parent (contract does it); `Utf8` encoding object becomes unnecessary |
| 2 | `Files/ReadTextFileTool.cs` | `ReadTextFileTool` | 45,90 | `DE`,`RL` | `File.ReadLinesAsync` → `RL`; the charter fixes the same `\n` split and `\r` drop |
| 3 | `Files/ReadMultipleFilesTool.cs` | `ReadMultipleFilesTool` | 52 | `RT` | UTF-8 no BOM on read |
| 4 | `Files/ReadImageFileTool.cs` | `ReadImageFileTool` | 44,52,64 | `DE`,`GE`,`RB` | `new FileInfo(...).Length` size guard → `GE.Length` |
| 5 | `Files/EditFileTool.cs` | `EditFileTool` | 48,86 | `RT`,`WT` | CRLF normalization stays in the tool; writes must be UTF-8 no BOM |
| 6 | `Files/DeleteFileTool.cs` | `DeleteFileTool` | 37,45,53 | `DE`,`GE`,`DF` | "is a directory" guard must keep working when the path is absent |
| 7 | `Files/DeleteDirectoryTool.cs` | `DeleteDirectoryTool` | 41,47,57,63 | `FE`,`DE`,`LE`,`DD` | `EnumerateFileSystemEntries(...).Any()` → `LE`; `DD(recursive:false)` must still throw on a non-empty directory |
| 8 | `Files/CreateDirectoryTool.cs` | `CreateDirectoryTool` | 37,44 | `DE`,`CDD(ownerOnly:false)` | idempotency message depends on `DE` |
| 9 | `Files/MoveFileTool.cs` | `MoveFileTool` | 41,42,47,56,60 | `DE`,`FE`,`MV` | **two different moves**: `Directory.Move` (56) has no contract member — `MoveAsync` is file-oriented (§7.2). Destination-exists check must not race |
| 10 | `Files/ListDirectoryTool.cs` | `ListDirectoryTool` | 44,54 | `DE`,`LE` | `EnumerateFileSystemInfos` → `LE` |
| 11 | `Files/DirectoryTreeTool.cs` | `DirectoryTreeTool` | 48,70,73,95 | `DE`,`LE`,`Rel` | recursive walk with budgets; `FileSystemInfo` type disappears from the tool |
| 12 | `Files/SearchFilesTool.cs` | `SearchFilesTool` | 50,77,80,101 | `DE`,`LE`,`Rel` | walk with skip rules; check `ListingOptions` gap (§7.2) |
| 13 | `Files/GrepFilesTool.cs` | `GrepFilesTool` | 108,109,143,146,166,270 | `FE`,`DE`,`LE`,`Rel`,`OR` | line 270 is a `FileStream` with `FileShare.ReadWrite`, 64 KB buffer and sequential scan — **no share/buffer member** (§7.2); do not silently degrade to `File.ReadAllText` on large files |
| 14 | `Files/GetFileInfoTool.cs` | `GetFileInfoTool` | 39 (line 40 is **exception 8**, see §7.3) | `DE`,`GE`,`GA` | reports size/time/attributes for files and directories; `FE`/`GE`/`GA` cover everything except the construction retained by exception 8 |
| 15 | `Archives/ZipCreateTool.cs` | `ZipCreateTool` | 49,55,60,108,135,141,155,191 (+ `new FileInfo` 110,175) | `DE`,`FE`,`MV`,`LF`/`LFR`,`DF`,`GE` | `Directory.EnumerateFiles(resolved, "*", options)` → `LFR`; temp zip then `Move(overwrite:true)` (108) relies on replace semantics; cleanup at 191 |
| 16 | `Archives/ZipExtractTool.cs` | `ZipExtractTool` | 52,57,62,117,140 | `DE`,`FE`,`CDD`,`WB` | per-entry overwrite policy (117) must keep the "refuse, do not clobber" default; parent creation at 140 |
| 17 | `Archives/ZipListTool.cs` | `ZipListTool` | 49,54 | `DE`,`FE` | read-only |
| 18 | `Archives/ZipReadTool.cs` | `ZipReadTool` | 41,46 | `DE`,`FE` | read-only |
| 19 | `Process/ProcessRunner.cs` | `ProcessRunner` | 52 | `DE` | plus `IPath` per §2.2; process start stays |
| 20 | `Triggers/TriggerWaiter.cs` | `TriggerWaiter` (+ `ConditionState`) | 95,100,221 | `DE`,`DirName`,`GE` | **`FileSystemWatcher` at 104 and the `FileInfo` snapshot at 221 have no contract member** (§7.2): the polling recheck can use `GE` (`Length`, `LastWriteTime`), the watcher cannot move behind `IFileSystem` without a new member |
| 21 | `AI.Mcp.CSharp/Scripts/ScriptRunner.cs` | `ScriptRunner` | 197,232,238 | `FE` | `Directory.SetCurrentDirectory`/`GetCurrentDirectory` at 347,357,370 and 355 stay as **exception 2**; `MetadataReference.CreateFromFile` reads the assembly itself and keeps a real path |
| 22 | `AI.Desktop/JsonClientSettingsStore.cs` | `JsonClientSettingsStore` | 14,15,32,33,34 | `FE`,`RT`,`CDD`,`WT`/`AFW`,`MV` | desktop start-up settings: temp-then-`Move(overwrite:true)` |
| 23 | `AI.Desktop/JsonThemePreferenceStore.cs` | `JsonThemePreferenceStore` | 13,14,26,27,28 | same | same pattern |
| 24 | `AI.Desktop/JsonWindowPlacementStore.cs` | `JsonWindowPlacementStore` | 15,28,29,30 | same | same pattern |
| 25 | `AI.Desktop/JsonWorkspaceLocationStore.cs` | `JsonWorkspaceLocationStore` | 14,15,33,36,37 | same | same pattern |
| 26 | `AI.Desktop/SharedHostLocator.cs` | `SharedHostLocator` | — (Path only) | `IPath.Full`/`Cmp` | per §2.2; no IO |
| 27 | `AI.Web/**` | — | 0 OS calls | none | verification only: confirm the web client still reaches storage through `AI.Server` and adds no `File.*` |

After each of 4.1/4.2: `dotnet run --project build -- test`, and one manual smoke of
`mcp/AI.Mcp.BuiltIn` + `mcp-csharp/AI.Mcp.CSharp` startup from a published layout (AC4/AC6 cover the
build; the published layout is not covered by tests, so Dan verifies it once at the end and Eva records
it as unverified-by-test if it is not).

## 5. Test classification (`tests/**`)

Every file that creates a temporary directory, touches the disk or starts a process. "Unit-convertible"
names the behaviour under test and the fake that replaces the disk; "integration" names the platform
behaviour genuinely under test. Files already using `MemoryFileSystem` and not touching the disk are
listed at the end for completeness.

### 5.1 Unit-convertible (Eva · QA converts these to `MemoryFileSystem`/`InMemoryPath`)

| File | Behaviour under test | Fake replacing the disk |
| --- | --- | --- |
| `tests/AI.Server.Tests/Infrastructure/Storage/JsonChatRepositoryTests.cs` | chat JSON read/write/move | already `MemoryFileSystem` |
| `tests/AI.Server.Tests/Infrastructure/Storage/JsonGlobalSettingsRepositoryTests.cs` | settings round-trip, absent file | already `MemoryFileSystem` |
| `tests/AI.Server.Tests/Infrastructure/Storage/ChatGraphStorageTests.cs` | graph persistence, ordering | already `MemoryFileSystem` |
| `tests/AI.Server.Tests/Application/Skills/SkillCatalogTests.cs` | skill discovery over a file set | already `MemoryFileSystem` (+ `InMemoryPath` for ids) |
| `tests/AI.Server.Tests/Application/Instructions/StandingInstructionsTests.cs` | instruction file selection | `MemoryFileSystem` |
| `tests/AI.Server.Tests/Infrastructure/Credentials/ProtectedGlobalSecretStoreTests.cs` | secret store read/write | `MemoryFileSystem` |
| `tests/AI.Server.Tests/Application/Chats/ChatKindExtensionTests.cs` | run-repository default | `MemoryFileSystem` |
| `tests/AI.Server.Tests/Infrastructure/Usage/TokenUsageTests.cs` | ledger aggregation | `MemoryFileSystem`; `Path.GetTempPath()` at 407 is only a **mock string** for `RootDirectory` (no IO) — a false positive for AC3 |
| `tests/AI.Server.Tests/Infrastructure/Tools/AppToolTests.cs` | `ask_user` path/repository payload | `Path.GetTempPath()` at 915,921,937 is only a value passed through the tool (no IO) — false positive for AC3 |
| `tests/AI.Server.Tests/Infrastructure/Storage/ResourceAssetServiceTests.cs` | asset store/read/delete, temp-then-move | `MemoryFileSystem` + `InMemoryPath`; today builds a temp dir for the *layout* only |
| `tests/AI.Server.Tests/Infrastructure/Workspace/WorkspaceUndoServiceTests.cs` | undo/redo of recorded file states | `MemoryFileSystem` + `InMemoryPath`; the file content is the only fixture |
| `tests/AI.Server.Tests/Infrastructure/Workspace/WorkspaceFileSearchTests.cs` | search roots, exclusion of `bin`, relative paths | `MemoryFileSystem` + `InMemoryPath` (search is pure over entries) |
| `tests/AI.Server.Tests/Application/Resources/WorkspacePathResolverTests.cs` | resolution of written paths against roots | `InMemoryPath`; the *non-integration* class only — the nested class tagged at line 12 keeps its temp dirs |
| `tests/AI.Server.Tests/Application/Resources/FilePreviewServiceTests.cs` | format selection, text/bytes preview, size guard | `MemoryFileSystem`; nested integration class (trait at 12) keeps the real disk |
| `tests/AI.Server.Tests/Application/Resources/ResourceServiceTests.cs` | resource registration/listing, "private content must not leak" | `MemoryFileSystem`; nested integration class (trait at 16) keeps the real disk |
| `tests/AI.Server.Tests/Application/Resources/ReviewServiceTests.cs` | review persistence and diff resources | `MemoryFileSystem`; nested integration class (trait at 13) keeps the real disk |
| `tests/AI.Server.Tests/Hosting/BrowserAccessServiceTests.cs` | hash list written/read and token non-leak | `MemoryFileSystem` + `AFW`; the whole file is tagged at line 9 today, so the tag moves to the part that needs the real platform |
| `tests/AI.Server.Tests/Updates/UpdateManagerTests.cs` | update state machine (`Available`/`Ready`/`Installing`), result-file handling | `MemoryFileSystem` for the state/result logic; the download/install portions stay integration |
| `tests/AI.Server.Tests/Infrastructure/Storage/ChatExecutionTests.cs` | run/session behaviour, scratch cleanup | mostly `MemoryFileSystem` already (2459); the two `Directory.Exists(scratch)` assertions at 1981,1999 are the only real-disk touch and stay integration |
| `tests/AI.Server.Tests/Infrastructure/Tools/BuiltInToolTests.cs` | tool semantics (delete, tree budgets, non-ASCII, guard) | `MemoryFileSystem` + `InMemoryPath` for the tool logic; `PathGuard`'s link/real-path behaviour stays integration |

### 5.2 Integration (platform behaviour is genuinely under test — keep, and tag `Category=Integration`)

| File | Platform behaviour genuinely tested |
| --- | --- |
| `tests/AI.Server.Tests/Infrastructure/Storage/PhysicalTextFileSystemTests.cs` | the adapter: replace-under-reader, permissions, retry (`File.Replace`, `File.SetAttributes`) |
| `tests/AI.Server.Tests/Infrastructure/Storage/PhysicalDirectoryBrowserTests.cs` | real directory listing, case, hidden/system entries |
| `tests/AI.Server.Tests/Infrastructure/Storage/DataDirectoryLockTests.cs` | OS lock semantics (`FileShare.None` / `flock`) — a second process must be refused |
| `tests/AI.Server.Tests/Infrastructure/Credentials/MasterKeyTests.cs` | POSIX file modes (`File.GetUnixFileMode`, `Directory.CreateDirectory(..., mode)`) |
| `tests/AI.Server.Tests/Infrastructure/Logging/JsonLineFileLoggerProviderTests.cs` | real append and rotation by last-write time |
| `tests/AI.Server.Tests/Infrastructure/Tools/TriggerWaiterTests.cs` | `FileSystemWatcher` events, real OS process CPU/memory and exit |
| `tests/AI.Server.Tests/Infrastructure/Tools/ExternalToolSessionTests.cs` | starting the real MCP child process |
| `tests/AI.Server.Tests/Infrastructure/Tools/McpToolSessionTests.cs` | MCP session over a real transport |
| `tests/AI.Server.Tests/Infrastructure/Tools/AppSkillsToolTests.cs`, `AppSubtaskToolTests.cs` | compose real server objects; keep tagged (trait at 22/32) |
| `tests/AI.Server.Tests/Application/Resources/FilePreviewEndpointTests.cs` | real Kestrel endpoint + range requests |
| `tests/AI.Server.Tests/Hosting/ServerStartupTests.cs` | real server start-up, data-directory lock, restart |
| `tests/AI.Server.Tests/Infrastructure/Workspace/GitBrowserTests.cs` | real `git` CLI (a fake runner exists for part of it; the repository fixture is real) |
| `tests/AI.Server.Tests/Infrastructure/Workspace/WorkspaceChangeTrackerTests.cs` | real file-watch/git-tracked workspace; `CreateTempSubdirectory` at 16,229,245 |
| `tests/AI.Mcp.CSharp.Tests/ScriptRunToolTests.cs` | the MCP C# server process and its working-directory effect (`Directory.GetCurrentDirectory`) |
| `tests/AI.Web.Tests/CompositionTests.cs` | real composition/DI wiring |
| `tests/AI.TextCorrection.Tests/HunspellWordLexiconTests.cs`, `TextAutoCorrectionTests.cs`, `TextCorrectionTests.cs`, `TextCorrectionPreparationTests.cs`, `SpellingCorrectionTests.cs`, `TrigramIndexTests.cs` | `Category=Slow`, read Hunspell dictionaries from disk; not related to this migration but they are AC3 hits — Eva must record them as "Slow/integration, unrelated to the FS abstraction" |

### 5.3 Already unit-level, no temp dirs (list for AC3 completeness)

`ChatKindExtensionTests`, `StandingInstructionsTests`, `SkillCatalogTests`, `ProtectedGlobalSecretStoreTests`,
`ChatGraphStorageTests`, `JsonChatRepositoryTests`, `JsonGlobalSettingsRepositoryTests`, `TokenUsageTests`,
`AppSkillsToolTests` (uses `MemoryFileSystem` but real server composition → see 5.2),
`AppSubtaskToolTests`, plus the whole `AI.Server.Tests/Infrastructure/Chat/**` and
`AI.TextCorrection.Tests` non-Slow files.

### 5.4 Screening patterns and counting note for AC1/AC3

This audit's §1 and Eva's acceptance report must count the same way, so **AC1 is screened with two
patterns** over `src/**/*.cs`, both at the commit under review:

1. **Call form** — the charter's own pattern, which produces the charter's 176:
   `\bFile\.(Exists|ReadAll|WriteAll|Delete|Move|Copy|Replace|Open|Create|AppendAll|GetLastWrite|SetUnixFileMode)`
   and `\bDirectory\.(Exists|Create|Delete|Move|GetFiles|Enumerate|SetCurrent|GetCurrent)`.
2. **Constructor / type / enumeration form** — the sites that number omits:
   `new (FileInfo|DirectoryInfo|FileStream)\(` and `FileSystemInfo` (including
   `DirectoryInfo.EnumerateFileSystemInfos` / `EnumerateDirectories`), which carry `Length`, `Attributes`,
   directory listing and the `PathGuard` / `ProjectPathAccess` link walk.

Expected screen result, so §1 and Eva's report stay comparable: pattern 1 → **196 lines in 54 files**;
patterns 1+2 → **237 operations in 56 files**. Each hit then gets a per-hit verdict — a hit performing no
disk I/O is a false positive, a hit on a documented exception (§7.3) is excluded by site, and the
residual violation count must be zero for AC1/AC2. The three added members (§7.2) are ordinary migration
sites, never exceptions. **Exceptions 5–8 in §7.3 are all excluded by site**, and two of them are inside
files that otherwise migrate — exception 5 in `FileMasterKeyStore.cs`, exception 8 in `GetFileInfoTool.cs`
— so those files are checked line by line, not excluded whole.

For **AC3** (`Path.GetTempPath` / `Directory.CreateDirectory` over `tests/**/*.cs`): `Path.GetTempPath()`
appears in 9 test files, `Directory.CreateTempSubdirectory` in 6, `Directory.CreateDirectory` in 12. Of
these, `AppToolTests.cs:915,921,937` and `TokenUsageTests.cs:407` perform **no IO** — they pass a path
string into a tool or a mock. AC3's grep will hit them; Eva records them as false positives with this
reference rather than tagging whole files as integration.

## 6. Verdict on `.worktrees/tests-modularity` (branch `tests/modularity` @ `6e4e1cca`)

Repository fact first: at audit time `tests/modularity` is an ancestor of `master` —
`git rev-list --left-right --count master...tests/modularity` returns `34  0`, so the branch has **no
commit that is not already in `master`**, and `git diff master tests/modularity` over
`src/AI.Server/Infrastructure/Storage/` and `tests/AI.Server.Tests/Infrastructure/Storage/` shows only
files where `master` is ahead (`PhysicalTextFileSystem.cs` −4, `ChatExecutionTests.cs` −161, …). The
worktree therefore contains an **older snapshot of `master`**, not competing work, and nothing there can
conflict with this migration: it is superseded by definition.

Per-file verdict:

| File in the worktree | Verdict |
| --- | --- |
| `.worktrees/tests-modularity/src/AI.Server/Infrastructure/Storage/ITextFileSystem.cs` | **Superseded.** Byte-identical to the current `master` file; the charter deletes it in favour of `AI.Contracts.FileSystem.IFileSystem`. Reuse value: the eight member *names* it fixes are exactly the ones the new contract keeps (that is where the charter's naming came from) |
| `.worktrees/tests-modularity/tests/AI.Server.Tests/Infrastructure/Storage/MemoryFileSystem.cs` | **Partially reusable.** It is the existing `ITextFileSystem` fake (`internal sealed`, `ConcurrentDictionary<string,string>`, `ReadPaths` observation queue, `FailWriteSuffix` failure injection). Bo's new fake must keep the two testing affordances — `ReadPaths` (observing what was read) and failure injection — because `JsonChatRepositoryTests`/`ProtectedGlobalSecretStoreTests` depend on them; the file itself must be rewritten to the frozen `IFileSystem` and is Bo's owned path, so nothing is copied from the worktree |
| any other file in the worktree | **Superseded** (older `master`). Do not modify, do not merge, do not copy from it |

Nothing in the worktree blocks the migration; no exclusion or merge action is needed beyond the
`.git/info/exclude` entry the charter states.

## 7. Risks

### 7.1 Silent-breakage risks, each with a test that would catch it

| # | Risk | Why it can break silently | Test that catches it |
| --- | --- | --- | --- |
| 1 | **Windows replace semantics under an open reader.** `MoveAsync(overwrite:true)` must keep the `File.Replace` path (`PhysicalTextFileSystem.cs:103–122`). A naive `File.Move(…, true)` or a fake that only overwrites a dictionary entry passes everywhere except production | readers of a JSON repository or the undo journal hold the target open; `File.Move` throws `IOException` while the reader is active, `File.Replace` does not | adapter test in `PhysicalTextFileSystemTests` (`Category=Integration`): open a read handle on the destination, then `MoveAsync(overwrite:true)`, expect success and new content; plus a contract test asserting the fake returns the new content |
| 2 | **Retry-on-transient-failure.** The current adapter retries `File.Move` (line 122) after antivirus/indexer interference | the retry is inside the deleted file; nothing else reproduces it | integration test opening the destination read-only for a short window and asserting the move eventually succeeds |
| 3 | **Link / junction / reparse points.** `PathGuard.cs:113–127` walks segments with `DirectoryInfo`/`FileInfo` and refuses escapes; `ProjectPathAccess.cs:21–24` does the same for project roots; `ChatTemporaryDirectory.cs:51,57,67` refuses a link as its root | if the walk is replaced by a canonicalization that *resolves* links, containment silently widens: a junction inside a granted directory could point outside it | integration test per guard: create a junction/symlink inside a granted root pointing outside it, assert access is refused; plus the same on the temp root |
| 4 | **Case and canonicalization differences.** Containment currently mixes `StringComparison.OrdinalIgnoreCase` (`ResourceAssetService.cs:165`) with a private per-platform `Comparison` (`PathGuard.cs:11`, `WorkspaceFileSearch.cs` `PathComparison`, `ArchivePaths.cs:16`, `TriggerWaiter.cs`) | using the wrong comparison makes `IsInside` either reject legitimate paths or accept siblings (`C:\data` vs `C:\database`) | `SystemPathTests` (`IPath.IsInside`) on both platforms with a case-only difference; a test asserting `IsInside("ab", "a")` is false |
| 5 | **`Path.GetFullPath` and the process current directory.** `ScriptRunner` changes the process cwd (exception 2), and `IPath.GetFullPath(relative)` resolves against it | a `GetFullPath` call moved to a different moment resolves a different path | `SystemPathTests` for the base-path overload; `ScriptRunToolTests` (integration) asserts the working-directory effect |
| 6 | **MCP published layout.** `DefaultToolSessionFactory.cs:23`, `CSharpToolSessionFactory.cs:27`, `UpdateInstaller.cs:15`, `UpdateManager.cs:170` look for `mcp/AI.Mcp.BuiltIn`, `mcp-csharp/AI.Mcp.CSharp`, `/opt/ai-client-csharp-mcp` | routing these through the contract without changing the *paths* is fine, but `FileExistsAsync` vs `File.Exists` differences (e.g. a directory matching a file probe) can flip a start-up branch | not covered by tests today; Dan's manual check on a published layout, recorded by Eva as evidence |
| 7 | **Encoding drift.** `JsonLineFileLoggerProvider.cs:57` uses `File.AppendAllText` (platform default), `File.OpenText` (`FilePreviewTextReader`) sniffs a BOM, the contract fixes UTF-8 no BOM | a BOM would appear in logs or a UTF-16 file would preview differently | unit test writing a non-ASCII line through `IFileSystem` and asserting bytes contain no BOM; a preview test with a UTF-16 file, marked integration if platform-dependent |
| 8 | **Directory enumeration options.** `WorkspaceFileSearch.cs:131,132,289,290`, `GitWorkspaceDiffReader.cs:44`, `ZipCreateTool.cs:155`, `DirectoryTreeTool.cs:73`, `GrepFilesTool.cs:146`, `SearchFilesTool.cs:80` pass `EnumerationOptions` (skip hidden/system, ignore inaccessible) / `SearchOption.AllDirectories` | `ListEntriesAsync` has no option parameter; a straight port loses skip rules and starts following `bin`/`obj`/hidden directories or throws on inaccessible ones | unit test over `MemoryFileSystem` asserting `bin`/`obj`/hidden entries are excluded; integration test enumerating a directory with an inaccessible child |
| 9 | **Exclusive lock weakened.** `DataDirectoryLock.cs:17` uses `FileShare.None`; `UpdateManager.cs:72` locks `owner.lock` | `OpenWriteAsync` cannot express "fail if another process holds this file"; two server instances could share one data directory and corrupt JSON state | `DataDirectoryLockTests` (integration): acquire twice, second must throw `DataDirectoryInUseException` |
| 10 | **Fake/production divergence.** `MemoryFileSystem` must reproduce the charter's edge-case table exactly (absent path → `null`/`false`, `DeleteFileAsync` on a directory throws, `MoveAsync(overwrite:false)` throws and leaves the source) | tests would pass against the fake and fail in production | `FileSystemContractTests` (Bo's owned file) parameterized over `MemoryFileSystem` and `SystemFileSystem` |
| 11 | **Recursive delete following links.** `ChatTemporaryDirectory.DeleteTree` refuses reparse points; `DeleteDirectoryAsync(recursive:true)` must not follow them | a scratch root replaced by a junction could delete outside the temp tree | integration test: junction inside the scratch tree pointing outside, delete the tree, assert the outside target still exists |
| 12 | **Scratch-directory privacy.** `ChatTemporaryDirectory.cs:55,59` create mode 700 | a `CreateDirectoryAsync(ownerOnly:true)` that ignores the flag on Unix would create a world-readable scratch dir | `ChatTemporaryDirectory` integration test asserting `File.GetUnixFileMode(dir)` (POSIX only) |
| 13 | **Link detection degraded to the `ReparsePoint` attribute.** Base checks `FileSystemInfo.LinkTarget is not null` at seven sites: `AI.Mcp.BuiltIn/Files/DirectoryTreeTool.cs:102`, `Files/GrepFilesTool.cs:174`, `Files/SearchFilesTool.cs:118`, `Grants/PathGuard.cs:114`, plus `AI.Server/Application/Resources/ProjectPathAccess.cs:22`, `AI.Server/Infrastructure/Workspace/GitWorkspaceDiffReader.cs:48`, `AI.Server/Infrastructure/Workspace/WorkspaceFileSearch.cs:299` (`GetFileInfoTool.cs:53` is a read of the value retained by exception 8, and `PathGuard.cs:125` / `ProjectPathAccess.cs:24` are the resolve calls themselves). The frozen contract exposes no link member, so a naive migration to `(attributes & FileAttributes.ReparsePoint) != 0` changes behaviour: a non-link reparse point (a Windows cloud-sync placeholder) has `ReparsePoint` set and a **null** `LinkTarget`, so a listing or search would silently stop descending into it, and a link could be mistaken for a plain directory | tests are platform-dependent, so a silent change survives CI on the other OS | **Mitigation (lead ruling):** keep the `ReparsePoint` attribute as a cheap pre-filter and, for entries that carry it, treat the entry as a link only when `ResolveLinkTargetAsync` returns a path different from its own plain canonical path — a segment that resolves to itself is not a link. Dan · Backend implements exactly that at the three MCP tool sites and in `PathGuard`; **Cleo · Backend applies the same pre-filter-plus-resolve-compare rule at the three `AI.Server` sites**, so the risk is not confined to one owner. **Catching test goes with §7.1 #3's containment test:** a placeholder-style entry must still be listed and searched (directory tree and grep/search results include it), and a junction inside a granted root pointing outside it must still not become a way out |

### 7.2 Lead rulings on the audit's gap list (closed — no unstoppable site remains)

The audit reported seven site classes that no frozen member could express. The lead has ruled on every
one of them. After the rulings — three contract additions below, documented exceptions 5–7 in §7.3, and
synchronous link detection handled as a migration note — **every site in §1–§4 is either migratable or an
exception**. This section therefore lists no remaining gap; the rows are the rulings themselves.

| Ruling | Member added by Bo · Architect | Sites | Default behaviour that must reproduce today's |
| --- | --- | --- | --- |
| Contract addition 1 (user chose a targeted extension) | share-mode / buffer option on the read path | `AI.Server/Infrastructure/Workspace/FileExcerptReader.cs:19,40`, `WorkspaceInstructionFileReader.cs:29`, `AI.Mcp.BuiltIn/Files/GrepFilesTool.cs:270` | `FileShare.ReadWrite \| FileShare.Delete` — a file a writer holds stays readable; `GrepFilesTool` keeps its 64 KB sequential buffer |
| Contract addition 2 | enumeration options | `WorkspaceFileSearch.cs:131,132,289,290`, `GitWorkspaceDiffReader.cs:44`, `ZipCreateTool.cs:155`, `DirectoryTreeTool.cs:73`, `GrepFilesTool.cs:146`, `SearchFilesTool.cs:80` | skip hidden/system attributes and `bin`/`obj`, ignore inaccessible entries, exactly as `EnumerationOptions` does today (§7.1 #8) |
| Contract addition 3 | `MoveDirectoryAsync` | `AI.Mcp.BuiltIn/Files/MoveFileTool.cs:56` (`Directory.Move`) | moving a directory, as distinct from the file-oriented `MoveAsync` |
| Exception 6, no member (§7.3) | — | `AI.Server/Infrastructure/Storage/DataDirectoryLock.cs:17`, `AI.Server/Updates/UpdateManager.cs:72` (`owner.lock`, `FileShare.None`) | exclusive, whole-file, process-scoped lock: a cross-process guarantee no in-process fake can model (§7.1 #9) |
| Exception 7, no member (§7.3) | — | `AI.Mcp.BuiltIn/Triggers/TriggerWaiter.cs:104` (`new FileSystemWatcher`) | file-change notification, including its overflow/error events; the `FileInfo` snapshot at `:221` is **not** covered and migrates to `GetEntryAsync` |
| Clarification, no member | — | `src/AI.Mcp.BuiltIn/Archives/*.cs` (`ZipFile.Open*`), `AI.Server/Application/Resources/ArchiveFilePreviewFormat.cs:13` | archive IO is not an OS file operation in the charter's glossary: keep `System.IO.Compression`, route only the surrounding existence/parent/move calls through the contract |

Because all three additions must reproduce today's default behaviour, the inventory rows for their sites
(§3 step 3 rows 6–8, 22–23; §4.2 rows 9, 12, 13, 20) stay **rows to migrate**, not exceptions.

**Migration note — synchronous link/reparse-point detection (not a gap).** `PathGuard.cs:125`,
`ProjectPathAccess.cs:24` and `ChatTemporaryDirectory.cs:51,57,67` need link/reparse-point detection
synchronously, while the contract offers `ResolveLinkTargetAsync` asynchronously. The lead ruled that
bridging the completed task is acceptable at these sites, with the containment invariants unchanged: a
segment walk that resolves links must still refuse a target outside the root, and the reparse-point
guards must still refuse a link as the scratch root. §7.1 #3 and #11 are the tests that would catch a
weakening, so they must survive phase 2.

### 7.3 Documented exceptions (charter list, plus the lead's additions, with exact sites)

1. `SystemFileSystem`, `SystemPath` adapters — Bo's files, plus the 16 calls in the deleted
   `PhysicalTextFileSystem.cs` that move into `SystemFileSystem`.
2. Process working directory: `src/AI.Mcp.CSharp/Scripts/ScriptRunner.cs:347,357,370` — stays.
3. Executable start-up/installation paths: `AppContext.BaseDirectory` in
   `Infrastructure/Tools/DefaultToolSessionFactory.cs:23`, `CSharpToolSessionFactory.cs:27`,
   `Updates/UpdateInstaller.cs:12`, `Updates/UpdateInstallationProvider.cs:14`,
   `Hosting/AiClientServer.cs:66`, `AI.Desktop/WindowsTaskbarBadge.cs:46`, `AI.Host/HostProcess.cs:15`;
   `Environment.GetFolderPath` in `CommandLine/ServerCommandLine.cs:13`,
   `Hosting/InstalledDesktop.cs:11`, `Infrastructure/Storage/PhysicalDirectoryBrowser.cs:111`,
   `Infrastructure/Tools/ChatTemporaryDirectory.cs:13,44`, `Updates/UpdateInstaller.cs:30`,
   `AI.Desktop/SharedHostLocator.cs:11` — stays.
4. Adapter tests tagged `Category=Integration` (see §5.2).
5. **POSIX file modes** (lead decision): `File.SetUnixFileMode` /
   `Directory.CreateDirectory(path, mode)` / `FileStreamOptions.UnixCreateMode` /
   `DirectoryInfo.UnixFileMode`, at `Infrastructure/Tools/ChatTemporaryDirectory.cs:11,59` and
   `Infrastructure/Credentials/FileMasterKeyStore.cs:31,40` and nowhere else.
6. **Exclusive file lock** (lead decision): `AI.Server/Infrastructure/Storage/DataDirectoryLock.cs:17`
   and `AI.Server/Updates/UpdateManager.cs:72` (`owner.lock`, `FileShare.None`). Reason: single-instance
   protection is a cross-process guarantee that no in-process fake can model; routing it through
   `OpenWriteAsync` would silently weaken it (§7.1 #9 keeps the test that guards it).
7. **File-change notification** (lead decision): `AI.Mcp.BuiltIn/Triggers/TriggerWaiter.cs:104`
   (`new FileSystemWatcher`). The metadata snapshot at `:221` is **not** covered — it migrates to
   `GetEntryAsync`, as listed in §4.2 row 20.
8. **Read-only platform metadata** (lead decision): `AI.Mcp.BuiltIn/Files/GetFileInfoTool.cs` — the
   `FileSystemInfo` construction (`FileSystemInfo info = directory ? new DirectoryInfo(resolved) :
   new FileInfo(resolved);` on base, line 40 there, line 50 in the current working tree) **plus its two
   kept reads**, `CreationTimeUtc` and the link name; every other operation in that file
   (`FileExistsAsync`, `GetEntryAsync`, directory-ness, attributes) is on the contract, and this is its
   only direct call. Cited by file plus symbol deliberately: the line numbers have already moved (base 40,
   working tree 50), so a line-number-only citation would read as wrong to whoever screens it. §4.2 row 14
   is aligned: its line 40 is excluded by site and only line 39 still migrates. Reason: `IFileSystem`
   exposes neither value, and deriving the link name through `ResolveLinkTargetAsync` would change
   user-visible output, because the base reports the *immediate* target of a link while the contract
   resolves a chain to its *final* target — a metadata tool must not quietly reword what it reports.

With exceptions 5–8 and the three contract additions of §7.2, no site in the inventory of §1–§4 keeps a
direct OS call for want of a member.

## 8. Hand-over notes

- **Eva · QA:** AC1 is screened with **both** patterns in §5.4 (call form → 196 in 54 files; call form +
  constructor/enumeration → 237 in 56 files), and every hit gets a per-hit verdict: no-IO hits are false
  positives, §7.3 names the excluded exception sites (5–8), and §7.2's three added members stay migration
  rows, not exceptions. Every remaining AC3 hit is classified in §5.1/§5.2; `AppToolTests.cs:915,921,937`
  and `TokenUsageTests.cs:407` are false positives (§5.4).
- **Cleo · Backend:** steps 1–2 of §3 first (13 consumers), then §3 step 3 in the given order —
  `ResourceAssetService` and `UpdateManager` are the heavy ones and both hinge on replace semantics.
  Cleo: apply the pre-filter-plus-resolve-compare rule at the three `AI.Server` link-detection sites,
  §7.1 #13.
- **Dan · Backend:** `PathGuard` (§4.1 #1) before any tool; the archive tools and `GrepFilesTool` are the
  ones where a mechanical port would change behaviour — `GrepFilesTool:270` and the enumeration sites now
  have contract members (§7.2 additions 1–2), and `MoveFileTool:56` uses `MoveDirectoryAsync` (addition 3).
- **Bo · Architect:** besides the frozen contract, add the three members of §7.2 (share-mode/buffer read
  option, enumeration options, `MoveDirectoryAsync`) with today's default behaviour; the fake must keep
  `ReadPaths` observation and failure injection from the existing `MemoryFileSystem` (§6), reproduce the
  charter's edge-case table, and carry `File.Replace` semantics for `MoveAsync(overwrite:true)` (§7.1 #1, #2).
