# ADR-013: One file-system contract for the product

Status: Accepted

Date: 2026-10-08

## Context

Every executable of the product touched the operating system directly: `File.*`, `Directory.*`,
`FileInfo`, `DirectoryInfo`, enumeration, `FileStream` and `FileSystemWatcher` appeared at 237 sites in
56 files (audit: [file-system abstraction audit](../analysis/filesystem-abstraction-audit.md)). Two
consequences followed from that, and both were being paid for already:

- a test could not exercise storage, tool or update logic without creating a real temporary directory,
  starting a process or holding a real lock, so those checks were integration tests by accident — slow,
  order-dependent and unable to run in parallel;
- the same containment and path rules were written by hand at each site, each with its own comparison
  and its own canonicalization, which is precisely where a security check drifts.

One abstraction already existed for part of this (`ITextFileSystem`/`PhysicalTextFileSystem`, 13
consumers) but covered text reads and writes only; the rest of the product had no seam at all.

## Decision

### One namespace, three interfaces

File access is expressed by `AI.Contracts.FileSystem` (`src/AI.Contracts/FileSystem/`):

| Type | What it is |
| --- | --- |
| `IFileSystem` | every operation on files and directories: existence and metadata, reads, writes, directory listing, mutation, link resolution |
| `IPath` | path algebra that carries platform semantics: separators, roots, comparison, containment, canonicalization |
| `IAtomicFileWriter` | a save that a crash or a power loss cannot leave half done |

`IFileSystem` is the only way the product reaches the disk. `IPath` exists because *pure* string algebra
(`Path.Combine` of a storage layout, a file extension) stays `System.IO.Path`: the seam is needed for the
subset whose behavior depends on the platform, not for string concatenation.

### Semantics the contract fixes

- Text is UTF-8 **without a BOM**; a document is written and read as bytes, so no platform default
  encoding can drift in.
- A read of an absent path answers with absence (`null`, `false`, an empty list) rather than throwing;
  the storage repositories already relied on that.
- A writer creates the missing parent directory. A mutation that names nothing either does what was
  asked of it (deleting what is already gone) or throws the documented type; each member says which.
- `MoveAsync(overwrite: true)` keeps the platform **replace** semantics: on Windows it goes through
  `File.Replace`, because a plain rename fails while a reader holds the destination open — and readers
  of a chat document genuinely do hold it open.
- Reads retry briefly: a document being replaced is unopenable for a moment, and a read that outlasts
  five attempts reports the failure with the path in it.

Naming follows one rule: a member says what it acts on. `FileExistsAsync`/`DeleteFileAsync` name the
kind, `MoveDirectoryAsync` is kept apart from the file-oriented `MoveAsync`, and nothing guesses the
kind of its argument.

### Three option-bearing members

The audit found three site classes no member could express, and each was added with a default that
reproduces today's behavior exactly:

1. `FileReadOptions` — how a read opens the document (`Share`, `BufferSize`, `FileOptions`).
   `FileReadOptions.Default` reproduces `FileShare.ReadWrite | FileShare.Delete`, so a read cannot make
   somebody else's save fail; `GrepFilesTool` keeps its 64 KB sequential scan.
2. `FileEnumerationOptions` — how a listing walks a directory (`Recursive`, `SearchPattern`,
   `SkipInaccessible`, `AttributesToSkip`, `SkipReparsePoints`). Nothing is filtered unless the caller
   asks, so a straight port cannot silently start descending into `bin`/`obj`. The pattern decides what
   is *reported*, never whether the walk descends: with `Recursive` on, a directory is entered whether or
   not its own name matches, so `*.md` finds a document inside a subdirectory that is not itself called
   `*.md`. Filtering the descent by the pattern instead would answer a recursive search with only its
   first level, which is a silent wrong answer rather than a failure.
3. `MoveDirectoryAsync` — moving a directory with its contents, which is not the same operation as the
   file-oriented `MoveAsync` and must not be guessed from the argument.

### Link resolution is real

`ResolveLinkTargetAsync` performs a genuine walk: each segment is resolved against the already-resolved
parent, and a segment that is a link, junction or symlink is replaced by its own final target, which the
rest of the path continues from. A path whose trailing segment does not exist is still resolved to where
it would land. `Path.GetFullPath` alone is **not** an implementation of this member.

The reason is containment. `PathGuard` (grants), `ProjectPathAccess` (project roots) and
`ChatTemporaryDirectory` (scratch roots) decide whether a path is inside a directory the caller may
touch, and a junction planted inside a granted directory points outside it while still *looking* like a
child of the grant. A canonicalization that resolves no link widens containment without any visible
change, which is exactly the failure mode a security check must not have. Callers bridge the completed
task synchronously; that is recorded here because containment checks are synchronous, and it is not an
invitation to block on asynchronous work elsewhere.

### Adapters, composition and fakes

- `SystemFileSystem`, `SystemPath` and `AtomicFileWriter` are the production adapters. Together with the
  exceptions below, `SystemFileSystem` and `SystemPath` are the only types in the product allowed to
  touch `System.IO`.
- `AI.Contracts.Composition` binds them as transients, so every executable that already links the shared
  setup (`DependsOn("AI.Contracts.Composition")`) inherits one file system without a second wiring.
- `MemoryFileSystem` and `InMemoryPath` (tests) are the in-memory counterparts. They model directories,
  content, timestamps and enumeration, and they **cannot** model a real link, a reparse point or a second
  process. `FileSystemContractTests` holds both implementations to one set of expectations; the behavior
  only the platform can show — following a link, replacing a file a reader holds open, refusing a
  non-empty directory — is asserted in the integration half of that file and tagged
  `Category=Integration`.

### Documented exceptions

These keep a direct operating-system call, by decision, with the exact sites:

| # | Exception | Sites | Why no member |
| --- | --- | --- | --- |
| 1 | the two adapters | `SystemFileSystem`, `SystemPath` | they *are* the platform seam |
| 2 | process working directory | `AI.Mcp.CSharp/Scripts/ScriptRunner.cs` | a process-global side effect of running a script, not a file operation |
| 3 | executable start-up paths | `AppContext.BaseDirectory` (`DefaultToolSessionFactory`, `CSharpToolSessionFactory`, `UpdateInstaller`, `UpdateInstallationProvider`, `AiClientServer`, `AI.Desktop/WindowsTaskbarBadge`, `AI.Host/HostProcess`); `Environment.GetFolderPath` (`ServerCommandLine`, `InstalledDesktop`, `PhysicalDirectoryBrowser`, `ChatTemporaryDirectory`, `UpdateInstaller`, `SharedHostLocator`) | they describe the running process, not data |
| 4 | adapter tests | tests tagged `Category=Integration` | the platform is the behavior under test |
| 5 | POSIX file modes | `Infrastructure/Tools/ChatTemporaryDirectory.cs:11,59`, `Infrastructure/Credentials/FileMasterKeyStore.cs:31,40` | a Windows test run cannot assert them, so a member would be a promise no test holds |
| 6 | exclusive file lock | `Infrastructure/Storage/DataDirectoryLock.cs:17`, `Updates/UpdateManager.cs:72` (`owner.lock`, `FileShare.None`) | single-instance protection is a cross-process guarantee no in-process fake can model; expressing it through `OpenWriteAsync` (which shares read on purpose) would silently weaken it |
| 7 | file-change notification | `AI.Mcp.BuiltIn/Triggers/TriggerWaiter.cs:104` (`FileSystemWatcher`) | notification, overflow and error events belong to the platform; the metadata snapshot at `:221` is **not** covered and goes through `GetEntryAsync` |
| 8 | link name and creation time of an entry | `AI.Mcp.BuiltIn/Files/GetFileInfoTool.cs` (the `FileSystemInfo`/`FileInfo` construction and its two reads, `CreationTimeUtc` and `LinkTarget`) | the contract deliberately models no link member and no creation time: `ResolveLinkTargetAsync` answers the *final* target of a chain, while `FileInfo.LinkTarget` reports the *immediate* one, so exposing it as a member would either change what the tool reports or add a second, weaker link concept to the contract. Everything else in that tool — kind, size, last-write time, the read-only bit, existence — already goes through `IFileSystem` |

With exceptions 5–8 and the three members above, no site in the audit keeps a direct call for want of a
member.

## Consequences

Positive:

- logic that used to need a temporary directory is testable in memory: storage, tools, previews, undo,
  resources and update state are unit tests now;
- containment has one implementation per concern and one comparison rule per platform
  (`IPath.IsInside`, `IPath.Comparison`), instead of a hand-built prefix test per file;
- a fake can inject failures (`FailWriteSuffix`) and observe reads (`ReadPaths`), so error paths are
  covered where they used to be untested;
- one binding in `AI.Contracts.Composition` replaces per-executable wiring.

Negative and accepted:

- the platform behavior of the adapters is only covered by integration tests, so those tests stay and
  must keep running in CI (`dotnet run --project build -- test-all`);
- a fake can be *wrong* in the same way as production is, which is why `FileSystemContractTests` asserts
  one set of expectations against both implementations rather than trusting either;
- the exceptions above are the places where the abstraction stops, and each one is a decision recorded
  here rather than an oversight.
