# Default MCP tools

## Composition

The built-in server ships 20 tools. All of them receive the `Ask` policy on first discovery, like any new tool.

In addition, the Host ships a second built-in server — [App tools](17-app-tools.md) with five tools over the application's own data.

| Tool | Purpose | Required capability grant |
|---|---|---|
| `process_run` | launch a program and wait for completion | — (not restricted by grants) |
| `fetch` | download an http/https URL as Markdown | — (network) |
| `list_allowed_directories` | enumerate project directory grants | — |
| `read_text_file` | read a text file, `head`/`tail` options | `read` |
| `read_multiple_files` | batch read up to 32 files | `read` |
| `list_directory` | direct children of a directory | `read` |
| `directory_tree` | flat directory tree walk | `read` |
| `search_files` | glob-pattern search | `read` |
| `grep_files` | substring or regex search in file contents | `read` |
| `get_file_info` | metadata without reading the contents | `read` |
| `write_file` | create or fully overwrite a file | `write` |
| `create_directory` | create a directory with parents | `write` |
| `edit_file` | targeted replacements with `dryRun` | `edit` |
| `move_file` | move and rename | `delete` for source, `write` for destination |
| `delete_file` | delete a file | `delete` |
| `delete_directory` | delete a directory; non-empty only with `recursive` | `delete` |
| `zip_list` | list archive entries, optional glob `pattern` | `read` |
| `zip_read` | read one archive entry as text | `read` |
| `zip_extract` | unpack entries into a destination directory | `read` for the archive, `write` for the destination |
| `zip_create` | pack files and directories into a new archive | `read` for the sources, `write` for the archive |

The following reference servers from `modelcontextprotocol/servers` are intentionally not adopted: `git` (12 tools are covered by `process_run`), `memory` (9 tools — a separate product decision about memory architecture), `sequentialthinking`, `time`, the deprecated `read_file`, as well as `read_media_file` and `list_directory_with_sizes` as excessive for the current scenarios.

## Usage

The Host automatically registers `Default tools` with a stable ID. The built-in server ships with the application in the `mcp` directory and runs over stdio. By default it is enabled with the `Ask` policy, but it is not transmitted to the model and is not started until it is explicitly discovered and enabled in a specific project's settings.

1. Select a connection that supports function calling in Chat Completions.
2. In the chat, ask the model to run a program with a working directory, for example: `Run dotnet --info in C:\Projects\DevTeam\AI`.
3. Review the program, arguments, directory, and timeout in the confirmation card. Click `Allow once` or `Deny`.
4. The call, its result, and the model's final response appear in history. The chat's stop button cancels a pending confirmation or an in-flight execution.

In project settings, the `Discover tools` button retrieves the tool descriptor through a real MCP `tools/list`. After discovery, `Ask`, `Allow`, `Deny`, the call count, and the timeout are available. Saving uses the existing project settings revision. A schema hash change returns a new tool to `Ask`.

The global server policy and the project policy apply together: any `Deny` forbids execution; auto-execution requires `Allow` at both levels. Disabling the server also forbids calls. The policy is re-checked after confirmation, immediately before execution.

## process_run

Input:

```json
{
  "executable": "dotnet",
  "arguments": ["--info"],
  "workingDirectory": "C:\\Projects\\DevTeam\\AI",
  "timeoutMs": 600000
}
```

`workingDirectory` is required and must be absolute. Arguments are passed as separate array elements; there is no implicit command shell. For a script, the shell is specified explicitly, for example `powershell.exe` with `-NoProfile`, `-NonInteractive`, `-Command`. Interactive stdin is closed.

The structured result contains `exitCode`, `stdout`, `stderr`, `durationMs`, `timedOut`, `truncated`, `error`. A non-zero exit code is recorded as the process result with MCP `isError` rather than being swallowed by an exception. Each stream is limited to 32768 characters; remaining data continues to be read and discarded so the process does not block.

Limits: up to 256 arguments, up to 65536 characters of arguments JSON on the Host side, up to 65535 calls of a single tool per run by default, up to 600 seconds per process and up to one hour for the agent loop, excluding time waiting for a person. There is no separate model-iteration limit; the number of parallel `tool_calls` in a single assistant message is capped at 1024. Tool policies inherit chat -> project -> global, with a default timeout of 600 seconds and an allowed range of 1..600 seconds. Saved shorter timeouts remain in effect.

For `process_run` and the optional C# server's `cs_run`, `timeoutMs` accepts 1..600000 milliseconds and defaults to 600000. Omit it to use the effective tool policy; an explicit value can shorten the run but cannot exceed that policy. The Host passes the effective run timeout to the server before confirmation. Other tools use the policy as a silence timeout renewed by progress, subject to their own internal limits. `fetch` retains its 30-second HTTP timeout, and `app_runs` accepts a wait window of up to 600 seconds.

## FileSystem tools

Project directory grants are passed to the server when the session is opened through the environment variable `AI_CLIENT_DIRECTORY_GRANTS` as JSON: `[{"root":"C:\\Projects\\Demo","recursive":true,"capabilities":["read","write","edit","delete"]}]`. Capability names match the `ToolNames` in `DirectoryGrant` written by the UI: `Read only` yields `read`, `Read/write` yields `read, write, edit, delete`.

A missing variable, an empty list, and unreadable JSON all mean no access: every FileSystem tool returns an error, and `list_allowed_directories` returns an empty list. This is fail-closed by default, including for `GET /api/mcp/default/tools`, where grants are not passed.

Path check in `PathGuard`:

- the path must be absolute, otherwise the Host rejects the arguments before confirmation; the `path`, `paths`, `source`, `destination`, and `workingDirectory` properties are canonicalized on the Host side so the user and the server see the same path;
- `Path.GetFullPath` removes `..` and `.`;
- every existing component of the path is checked for being a reparse point and replaced with its final target, so a symlink or junction inside a grant does not escape it;
- comparison against the grant root is case-insensitive on Windows; a non-recursive grant only allows direct children;
- the grant root is canonicalized the same way when the server starts.

Result limits: 262144 characters per file contents, 32 files and 262144 characters per `read_multiple_files`, 5000 entries per `list_directory`, 20000 entries and 32 levels per `directory_tree`, 1000 matches and 200000 visited entries per `search_files`, 1000 matches, 5000 read files, and 131072 characters per `grep_files`, 64 replacements per `edit_file`. Directory links are enumerated but not expanded during the walk. `move_file` does not overwrite an existing destination. `delete_file` deletes only a file, `delete_directory` only a directory: each tool refuses the other kind of path and names the suitable one. Without `recursive`, a non-empty directory is not removed, so a call that did not request recursion cannot wipe more than the named directory. Both tools declare `destructive = true` and `idempotent = false`: a repeat call on an already removed path returns `deleted: false` with an error.

`read_text_file` without `head`/`tail` returns the contents verbatim, including the trailing newline, so the result can be written back without distortion. `edit_file` compares text in normalized LF form and preserves the file's original newline style; every `oldText` must occur exactly once, otherwise no replacement is applied.

Not implemented: `IncludePatterns`/`ExcludePatterns` from the `DirectoryGrant` model, media file reading.

## zip_list, zip_read, zip_extract, zip_create

Archive tools work only inside the granted directories and reuse `PathGuard`: reading an archive and reading an entry need the `read` capability, writing an archive or extracting needs `write`. There is no access outside the grant roots, and a path outside every grant is refused before the archive is even opened.

`zip_list` returns `entryCount` and `entries` with `path`, `kind` (`file`/`directory`), `size`, `compressedSize`, and `modifiedAt`; the optional `pattern` is a glob over entry names, and a pattern that matches nothing yields an empty list rather than an error. `EntryCount`, `totalBytes` and `compressedBytes` describe the whole archive even when a cap or a pattern left fewer rows in `entries`. `zip_read` returns the contents of a single entry with `size` and `truncated`; a missing entry is an error that names `zip_list` as the way to see what the archive holds, and an entry that looks binary is reported as such rather than returned as garbled text. `zip_extract` unpacks the selected entries into `destination`, returns the written `files`, refuses to overwrite an existing file unless `overwrite` is true, and fails the whole call on a Zip Slip entry: an entry name never decides where a file lands, so a name that is rooted or climbs out with `..` is an error and nothing is written at all. The whole extraction is planned before the first write, so a call over the caps or colliding with an existing file extracts nothing rather than part of the archive. `zip_create` packs files and directories into a new archive, a directory contributing its files recursively under its own name with forward-slash entry names, skipping default service directories unless `excludeDefaults` is false, and writing to a temporary sibling moved into place, so a failed call leaves no half-written or destroyed archive.

Limits: 5000 entries and 131072 characters per listing, 262144 characters per entry read, and 10000 entries, 1024 sources and 268435456 uncompressed bytes per extraction or pack. Extraction is limited further by what is already on disk and by `pattern`, which is the way through an archive over the cap; the pack result lists at most 2000 written files and sets `truncated` instead of dropping the rest silently.

Not implemented: archive formats other than zip, entry comments and extra fields, encrypted or password-protected archives, and `read_media_file`-style extraction of a single entry to disk.

## grep_files

Content search. `path` points to a file or to a directory walked recursively; file selection is governed by `filePattern` and `excludePatterns`; service directories are skipped by default, as in `search_files`.

`query` is treated as a literal substring by default and compared case-sensitively; `ignoreCase` relaxes the comparison, `isRegex` switches to a regular expression. Regular expressions run in `RegexOptions.NonBacktracking` mode: runtime is linear in the length of the string, so a pathological pattern does not block the server, but backreferences and lookaround are not supported and are returned as an argument error rather than as an exception.

At most one match per line. `contextLines` adds the adjacent lines before and after; at file boundaries the arrays are shorter than requested rather than padded with empty lines. `maxMatchesPerFile` (default 5) caps the number of quoted lines, while `matchCount` stays as the full number of matched lines in the file — file coverage matters more than completeness of output for any single one. A global size budget sits on top; any hit sets `truncated: true`.

A file with a zero byte in its first 8192 bytes is treated as binary and skipped; the same goes for files larger than 16 MiB and unreadable files. Their count is returned in `filesSkipped`, the read ones in `filesScanned`. Contents are decoded as UTF-8 with BOM detection, so files in single-byte encodings are read with distortion. A line longer than 400 characters is returned as a window around the match with truncation markers, while `column` remains the position in the real line.

## fetch

Only the `http` and `https` schemes are allowed; credentials in the URL are rejected. Up to 5 redirects, 30-second timeout, no more than 5 MiB read. HTML and XML are converted to Markdown by extracting headings, links, lists, and text; `script`, `style`, `head`, and similar elements are dropped. This is a tag-level transformation, not a DOM one. `raw` returns the body without conversion; other types (JSON, plain text) are returned as is. On truncation, a repeat call with `startIndex` equal to the received `nextIndex` continues reading.

`robots.txt` is not requested, so the tool is not intended for site crawling. Private and loopback addresses are not blocked: `process_run` under the same account already provides full network access, so a separate SSRF guard here would not create a new boundary.

## Access and lifecycle

The process runs under the Host account's permissions. Working directory and directory grants are not a sandbox for arbitrary programs. Permission to run a process can grant it access to the files and network of that account.

The Host and the server pass only the selected system environment variables required to launch programs. Arbitrary Host variables and saved API credentials are not passed automatically. This does not stop the program itself from reading the credential files available to it.

On cancel or timeout, the process tree is terminated. On Windows a Job Object with `KILL_ON_JOB_CLOSE` is used: child processes that joined the job are also terminated when the server closes. This is lifetime management, not a security boundary. Background sessions and PTY are not implemented.

Each assistant tool call is recorded before execution, and the tool result before the next model request. History nodes preserve the provider call ID. When recovering or forking inside an unfinished pair, a message about the unknown result is added to the context. The previous call is not automatically re-executed. Network errors after a possible side effect stop the run.

## CLI

`session send` returns `status: awaiting_approval` and an `approval` object if a decision is required. The run keeps waiting on the Host; confirmation can be given in the Web or through a separate command:

```powershell
dotnet run --project src/AI.Cli -- session approve --session <id> --approval <approval-id> --allow true
```

To refuse, pass `--allow false`. The command returns the accepted decision; history can be read through `session show`. `GET /api/runs` contains the current state and new confirmation requests.

## Implementation and first-version boundaries

- `ModelContextProtocol.Core` 2.2.0 — client and server, initialization, stdio, discovery, and invocation.
- `JsonSchema.Net` 9.4.0 — input schema and structured result validation. The first version receives schemas only from the bundled server.
- `ProcessRunner` — standard Process API and process tree management on Windows.
- `ChatAgent` — sequential agent loop in the Application layer; `ChatRunDispatcher` retains control over queues, cancellation, revisions, and SSE snapshots.
- `DefaultToolSessionFactory` — one built-in server session per agent loop; the connection is recreated for each new run. Passes the project's directory grants to the server and canonicalizes path arguments before confirmation.
- `PathGuard` — directory grant check on the server side: absolute path, `..` removal, reparse-point resolution along the entire chain, containment, and capability.
- `HtmlText` — HTML to Markdown conversion using regular expressions, without a DOM and without external dependencies.

Pure.DI is retained. Microsoft.Extensions.AI and an agent framework are not introduced in the Application layer: the current integration uses the existing streaming Chat Completions adapter. Migrating adapters to ready AI abstractions remains a separate decision; a live prototype against an external paid endpoint was not performed.

Third-party stdio/HTTP server execution, OAuth, dynamic `list_changed`, the Responses API, History tools, and background processes remain next stages. Their existing settings do not mean execution is already wired.

## Verification

The standard command is `dotnet run --project build -- verify`.

Tests cover the real stdio MCP with `dotnet --info`, the 20-tool composition, fragmented model calls, schema validation, confirm/refuse/policy withdrawal, history recovery, both output streams, non-zero exit code, timeout, cancellation, environment, and child termination.

For FileSystem tools and `fetch` additional checks cover: refusal when grants are missing, capability and containment (including non-recursive grant, `..`, and relative path), parsing and filtering of `AI_CLIENT_DIRECTORY_GRANTS`, Markdown extraction from HTML, and an end-to-end scenario through a real stdio — `create_directory`, `write_file`, `read_text_file` with `tail`, `edit_file` with `dryRun` and a repeat failed replacement, `read_multiple_files`, `search_files`, `list_directory`, `directory_tree`, `get_file_info`, `move_file`, `delete_file`, and `delete_directory` along with a denial of deletion under a grant without the `delete` capability, refusal of a path outside the grant, and rejection of a relative path before confirmation.

For the archive tools an end-to-end scenario through the real stdio packs a directory, lists the archive with and without a glob, reads one entry and a missing one, unpacks it, refuses a repeat extraction without `overwrite`, refuses a Zip Slip entry contained in a hostile archive, refuses a path outside the grant for both listing and creating, and reports a plain file as not a valid zip archive.

For the archive tools an end-to-end scenario through the real stdio packs a directory, lists the archive with and without a glob, reads one entry, unpacks it, refuses a repeat extraction without `overwrite`, refuses a Zip Slip entry contained in a hostile archive, refuses a path outside the grant for both listing and creating, and reports a plain file as not a valid zip archive.

The publish build includes the server under `mcp`.
