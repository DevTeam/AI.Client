# Storage

By default, data is located in `%LOCALAPPDATA%/AI`. `AI_CLIENT_DATA_DIRECTORY` sets a separate Host data directory, and the `--data-dir` command-line option overrides both (`AI.Host --help` lists all options).

Projects and chats use schema 2; runs use schema 4. Old formats are not supported and are not migrated. A new data directory is required; user files are not removed automatically.

The chat manifest contains `MessageIds` and branches. Messages are written separately to `<chatPath>.nodes/<messageId>.json`. Immutable nodes are written first, then the manifest is replaced atomically. A failure before the replacement leaves the previous history intact. Modifying an existing node is forbidden.

Repositories serialize revision checking and writing within a single Host. Multiple processes must not write to the same directory. Unused nodes are currently retained; a garbage collector is not implemented.

## File access

Storage logic performs no operating-system call of its own. Every read, write, directory listing, move
and delete goes through `AI.Contracts.FileSystem.IFileSystem`, which the platform adapter
`SystemFileSystem` implements and tests replace with an in-memory fake. Path rules that depend on the
platform — separators, roots, comparison, containment, canonicalization — go through `IPath`, so the
same code decides the same way on Windows and Unix and a test chooses the platform instead of
inheriting the runner's. A save that must survive a crash goes through `IAtomicFileWriter`: the content
lands in a temporary sibling of the target and is then moved into place with the replace semantics that
let a reader hold the document open meanwhile.

The semantics a caller may rely on:

- text is UTF-8 without a BOM;
- reading an absent path answers with absence (`null`/`false`) rather than throwing — a repository asks
  about a document it has not written yet;
- writers create the missing parent directory;
- `MoveAsync(overwrite: true)` replaces, and on Windows it must go through `File.Replace`: a plain
  rename is refused while a reader holds the destination open, and the reader is what made the save
  fail with an access denial that had nothing to do with permissions;
- `ResolveLinkTargetAsync` really follows links, junctions and symlinks; a canonicalization that
  resolves no link would let a junction planted inside a granted directory look like a child of the
  grant, which is why this is a security primitive rather than a convenience;
- a listing's search pattern decides what is reported, not how far the walk goes: a recursive listing
  enters every subdirectory and matches the pattern against each name, so `*.md` finds a document that
  lives in a subdirectory named something else.

What deliberately stays a direct platform call — process working directory, executable start-up paths,
POSIX file modes, the single-instance file lock, file-change notification, and the link name plus
creation time a single tool reports — is listed with its exact sites in
[ADR-013](decisions/ADR-013-file-system-contract.md).

Connections and MCP settings are written to a single `settings.json`. Secrets are protected by the user account.

A command remains in the queue until the response is committed. Retries reuse the original message ID and a deterministic response ID. This prevents duplication of saved history but does not guarantee a single external HTTP request in case of failure.
