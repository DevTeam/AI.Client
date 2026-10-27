---
id: code-run-shell
name: Code run shell
aliases: ["shell","cli","powershell","pwsh","process-run"]
icon: code-run-shell
kind: playbook
description: Run programs and shell commands through process_run on Windows, Linux or macOS, including PowerShell; may change files or external data only within the user's authorized scope, with risk, duration and encoding checks.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"The command or automation goal"},"shell":{"type":"string","description":"Shell explicitly chosen by the user"},"workingDirectory":{"type":"string","description":"Working directory provided by the user"}},"additionalProperties":false}
tools: ["tool_search","run_skill","list_allowed_directories","process_run","process_wait","process_stop","trigger_wait","read_text_file","get_file_info","list_directory","write_file","create_directory","ask_user"]
---

For disposable scripts, logs or intermediate results, find the `purpose: "chatTemporary"` root with
`list_allowed_directories` when available and verify the executor can access it. Keep final
deliverables at the requested path; inspect only relevant excerpts of verbose output while
preserving the exit code. Remove task-created scratch files when done.

The user's instructions take precedence. Follow repository instructions and use ordinary
permission-checked tools; this playbook grants no access. Report in the user's language.
Prefer a direct executable and argv array. Use a shell for its built-ins, pipelines, redirection
or scripts, and a dedicated tool when it better serves the task.

Use `process_run` by default for commands, builds, tests and installed CLI tools. It runs with
the host user's permissions and follows the effective tool policy and command rules. Use
`process_run_sandboxed` only when the user explicitly requests sandboxed execution. For CLI
configuration or a denied directory in that explicit sandbox, load `code-cli-configure` when needed.

1. Establish the goal, inputs and authorized side effects. Check `process_run` availability,
   discovering it through `tool_search` if needed, and read its current description/schema.
   If unavailable, briefly explain and use another available executor; do not install/connect
   tools automatically. Identify the server OS, not the client's OS, and verify executable paths,
   shell availability/version and working directory with permitted read-only checks.
2. Use `executable`, string-array `arguments`, verified `workingDirectory` and supported
   `timeoutMs` and `waitTimeoutMs`. In the known contract, a bare executable name is resolved through PATH even with
   a working directory; use an absolute path or `./name` for a file in that directory. Each array
   element is one argv entry: do not wrap a path in extra quotes just because it contains spaces.
   There is no implicit shell, glob or variable expansion; `*`, `~`, `$VAR`, `%VAR%`, pipes and
   redirects are literal arguments unless a shell explicitly parses them. Stdin is closed at
   launch: use noninteractive modes and supported input files, not interactive prompts. Use `process_wait` and `process_stop` for managed continuation of either launch variant.
   Do not invent stdin, environment, encoding or detached execution parameters; the actual schema wins.
3. Choose the shell explicitly when required:
   - Windows PowerShell 7: `pwsh` with `-NoLogo`, `-NoProfile`, `-NonInteractive`, `-Command`,
     then one string of PowerShell code; or use `-File` for a verified .ps1 file.
   - Windows PowerShell 5.1: `powershell.exe` with the same switches, but use syntax and encoding
     supported by that version. PowerShell 7 is not guaranteed to be installed.
   - Windows cmd: `cmd.exe` with `/d`, `/s`, `/c`, then cmd code for cmd built-ins or .cmd/.bat
     files. Respect cmd-specific quoting and percent expansion.
   - Linux/macOS POSIX shell: verified `/bin/sh` with `-c`, then POSIX code; avoid Bash extensions.
     For Bash-specific code use verified `bash` with `--noprofile`, `--norc`, `-c`, then Bash code.
     Do not assume a particular Bash version, especially on macOS. Use zsh only when appropriate
     and verified. `pwsh` also works on Linux/macOS when actually installed.
   Respect host path syntax, case sensitivity, executable extensions and line endings. Avoid
   interactive profiles unless required. On Windows keep recursive filesystem operations in one
   shell, using PowerShell -LiteralPath where applicable; do not enumerate there and delete via cmd.
4. For complex PowerShell use a .ps1 with `param(...)`, `-File` and values as separate arguments.
   Example argv: `["-NoLogo","-NoProfile","-NonInteractive","-File","C:\\work\\job.ps1",
   "-InputPath","C:\\work\\данные.json"]`; first verify those actual paths or use the host's paths.
   `-Command` receives code to be parsed: an argv array does not protect interpolated data inside
   that code. For POSIX shell pass data as positional arguments: `sh -c '<code using "$1">'
   task '<value>'`. Use `--` for values beginning with '-' only when the program supports it.
   JSON escaping is not shell escaping. PowerShell -EncodedCommand is Base64 of UTF-16LE code,
   not protection against injection or an output-encoding setting. Do not automatically bypass
   ExecutionPolicy. For scripts requiring stop-on-error set `$ErrorActionPreference = 'Stop'`;
   check `$LASTEXITCODE` immediately after important native programs and propagate an appropriate
   `exit` code. Nonterminating PowerShell errors and native program failures need separate checks.
5. Assess deletion, overwrite, moves, database/network changes and disk exhaustion before launch.
   `process_run` uses the host user's permissions; its working directory and project directory
   grants are not a boundary. Keep the command within the authorized task scope. When sandboxed
   execution was explicitly requested, `process_run_sandboxed` is confined to session grants;
   respect that request and report any limitation rather than silently switching modes.
   Verify full targets, empty inputs, source/destination
   overlap, existing outputs and symlinks/junctions. Before recursive deletion/moves confirm the
   final path is within the intended authorized area, not an accidental root, home or entire
   working tree. Preview targets or use dry-run/-WhatIf where supported; preserve important
   originals or write and verify a new output before replacement. Inspect the entire command:
   redirection can truncate a file before the program succeeds. Do not add blanket approval for
   ordinary reads or already authorized changes. Prepare concrete targets and ask only for missing
   authorization for irreversible actions outside the request. Declined stops that action;
   unresolved approval does not authorize it. Process termination does not undo partial changes.
   Never stop, kill or restart a process this run did not start — another chat's tests or build,
   an IDE, a server. A timeout, a hang, a locked output file and an earlier approval for other
   work are not permission: report the process holding it, because freeing it is the user's
   decision. Use `process_stop` only for a handle this MCP session started and when stopping
   that work is within the user's authorized task. Stopping does not undo partial effects.
6. Separate the response wait from the process deadline. `waitTimeoutMs` defaults to 10000 on
   both launch tools (0..60000). It returns `status: running` and `processId` without stopping
   the command. `timeoutMs` remains the hard tree deadline (1..86400000, default 600000), bounded
   by the effective tool policy. Omit it unless an intentionally shorter deadline is needed.
   For `running`, prefer one bounded `trigger_wait` with type `process_completed`,
   `processSessionId` set to the returned handle, and its `stdoutOffset` and `stderrOffset`.
   Read the nested `processResult` for status, exit code and new output; `triggered` alone is not
   command success. Use `process_wait` for intermediate output or if triggers are unavailable.
   Discover the needed tool if it is not offered. Waits may expire repeatedly; this is
   not a test failure. Never restart or split a test suite merely because a wait expired, and do
   not infer a hang solely from silence. Continue until a terminal result establishes the outcome.
   `process_wait` defaults to a 60000 ms wait; cancellation ends the wait, not the process. A new
   user message may end the wait early: read it before deciding whether to wait again.
   `process_stop` explicitly stops the owned tree. Session shutdown also stops remaining work,
   so finish verification before the final answer. Handles do not survive a new MCP session.
   The states `stopped`, `timed_out` and `failed` do not establish successful verification.
   Only after a diagnosed failure, resource limit or actual hard deadline should you consider
   targeted reruns or safely separable batches. Respect the current deadline policy rather than
   bypassing it with nohup, &, detached processes or Start-Process.
7. Agree encodings for input files, stdout/stderr and artifacts. JSON argv is Unicode, but child
   byte streams and files may use UTF-8, OEM/ANSI or UTF-16. Prefer explicit UTF-8 modes when the
   program supports them; agree with the tool's decoder rather than assuming UTF-8. If the decoder
   cannot be configured, capture raw output to a file using byte-preserving means and read it
   with a known encoding through an available file tool or controlled decoder. Do not repair text
   after replacement characters have lost information. Check a short Cyrillic/non-ASCII example
   when important. Preserve existing encoding, BOM and line endings; default new text to UTF-8
   without BOM unless the consumer needs another format. Do not expose secrets in argv or logs.
   - PowerShell 7 usually writes UTF-8 without BOM; Windows PowerShell 5.1 defaults vary by cmdlet,
     and Out-File/> commonly write UTF-16LE. Specify -Encoding with a value supported by the version.
     For .ps1 with non-ASCII under 5.1 use UTF-8 with BOM or another correctly readable encoding.
     Use .NET file APIs when an exact byte format is needed.
   - `[Console]::OutputEncoding` governs console output; `$OutputEncoding` governs text sent to
     native programs through pipelines. Set them locally only as needed and agree with decoding.
     Neither `chcp 65001` nor a PowerShell encoding assignment forces every native program to
     emit UTF-8. Avoid `cmd /u` unless UTF-16 output from cmd built-ins matches the decoder.
   - On Linux/macOS verify an available UTF-8 locale rather than assuming C.UTF-8 exists. The known
     runner restricts inherited environment variables; do not assume LANG or arbitrary credentials
     survive. Use a supported program/wrapper mechanism for required environment, without changing
     the server globally. Locale also affects dates, decimal separators and sorting; use explicit
     machine formats when needed.
8. Inspect `exitCode`, `error`, `timedOut`, `truncated`, stdout and stderr. A nonzero code depends on
   the program's contract (for example a search with no matches); absence of stderr is not proof of
   success. Each response contains at most 32768 original bytes per stream. `stdoutPath` and `stderrPath`
   retain the full original byte logs in the chat temporary directory, in the program's encoding.
   `stdoutOffset` and `stderrOffset` are byte cursors; pass them to the next wait. If
   `hasMoreOutput` is true after completion, use `process_wait` with `waitTimeoutMs: 0` to page
   the remaining output or inspect the saved logs. A truncated page is not a lost full log. Verify the final
   artifacts and intended postconditions. Before retrying failures inspect partial side effects.
   The final answer contains the requested command result or accessible artifacts, actual checks
   and any unresolved duration, encoding or completeness limits. A running job is not a passed run.
