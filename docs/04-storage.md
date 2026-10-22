# Storage

By default, data is located in `%LOCALAPPDATA%/AI`. `AI_CLIENT_DATA_DIRECTORY` sets a separate Host data directory, and the `--data-dir` command-line option overrides both (`AI.Host --help` lists all options).

## User directories

The data directory keeps what belongs to the installation — `keys/`, `logs/`, `updates/`,
`browser-access.txt` and the `.lock` file — and reaches it through `IInstanceStorageLocation`. Everything
a user stores lives under `users/<user id>/` and is reached through `IUserStorageLocation`, which reads
the user of the current scope on every call ([ADR-014](decisions/ADR-014-replaceable-boundaries-and-user-scope.md)):
`settings.json`, `command-rules.json`, `credentials/`, `project-order.json`, `projects/`, `memory/`,
`instructions/`, `resources/`, `assets/`, `usage/` and `skills/`. The paths below are relative to that
user directory. A Host without sign-in has one user, whose id is `00000000-0000-0000-0000-000000000001`.

A data directory written before user directories existed keeps these entries at its top level, where
the Host no longer looks; it shows an empty installation and leaves them untouched. Move them once, with
the Host and the desktop app stopped:

```bash
scripts/move-data-to-user.sh "$HOME/.local/share/AI"
```

```bat
scripts\move-data-to-user.cmd "%LOCALAPPDATA%\AI"
```

The script moves each entry on its own, skips an entry that is already in place, overwrites nothing and
changes no document, so an interrupted run can be repeated. It exits with 1 when an entry exists in both
places and reports it.

Projects and chats use schema 2; runs use schema 4. Old formats are not supported and are not migrated. A new data directory is required; user files are not removed automatically.

The chat manifest contains `MessageIds` and branches. Messages are written separately to `<chatPath>.nodes/<messageId>.json`. Immutable nodes are written first, then the manifest is replaced atomically. A failure before the replacement leaves the previous history intact. Modifying an existing node is forbidden.

Repositories serialize revision checking and writing within a single Host. Multiple processes must not write to the same directory. Unused nodes are currently retained; a garbage collector is not implemented.

## Binary content

Uploaded files, images and workspace Undo snapshots are bytes, not documents, and go through
`IBlobStore`: immutable, content-addressed keys such as `<project>/<sha256>.bin`, where a key that is
already taken keeps its first content. `DirectoryBlobStore` keeps them under the user's `assets/`, one
file per key; an object store such as S3 can stand in for it behind the same port
([ADR-014](decisions/ADR-014-replaceable-boundaries-and-user-scope.md)). Undo manifests, which change when
a file is undone, stay plain files beside them.

## Read caches

A chat document holds whole tool results, and the agent loop reads it several times around every tool
call, so `JsonChatRepository` keeps the parsed chats it has read or written, by path, least recently
used first out once their documents together pass 96 MB (a document above 48 MB is not kept). Each
entry carries the file's stamp: its length and last write time.

- A read whose stamp matches the entry gets a clone of the cached chat. `ChatThread` is mutable, and a
  caller changes what it read and then saves it, so the cached chat itself is never handed out; the
  clone shares the immutable messages, branches and policies.
- A read that misses parses the file and keeps the result only when the stamp did not change while it
  was read.
- A save checks the revision against the entry while its stamp matches the file, and reads the file
  otherwise. It keeps a clone of what it wrote, under the stamp the file has after the move, so the
  caller may go on changing its own instance. A conflicting save changes nothing in the cache.
- A write by anything else, another repository instance included, changes the stamp, so the entry is
  dropped at the next read and the stored revision is checked again from the file.

`JsonProjectRepository` keeps the last document text of each project the same way, so the project read
at every step of a run costs a stamp instead of a file read; the text is parsed for every caller.


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

The order the person gave the projects is `project-order.json` beside the `projects` directory (whose every `*.json` is read as a project): a JSON array of project ids, first to last. It is not part of a project, so moving one does not change its revision. `GET /api/projects` lists the projects in that order and the ones it does not name after them, by name; `PUT /api/projects/order` replaces it. A missing or damaged file reads as no order.

A command remains in the queue until the response is committed. Retries reuse the original message ID and a deterministic response ID. This prevents duplication of saved history but does not guarantee a single external HTTP request in case of failure.
