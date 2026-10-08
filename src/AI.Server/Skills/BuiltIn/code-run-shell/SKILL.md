---
id: code-run-shell
name: Code run shell
aliases: ["shell","cli","powershell","pwsh","process-run"]
icon: code-run-shell
kind: playbook
description: Run programs and shell commands through process_run on Windows, Linux or macOS, including PowerShell; may change files or external data only within the user's authorized scope, with risk, duration and encoding checks.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"The command or automation goal"},"shell":{"type":"string","description":"Shell explicitly chosen by the user"},"workingDirectory":{"type":"string","description":"Working directory provided by the user"}},"additionalProperties":false}
tools: ["tool_search","list_allowed_directories","process_run","read_text_file","get_file_info","list_directory","write_file","create_directory","ask_user"]
---

For disposable scripts, logs or intermediate results, find the `purpose: "chatTemporary"` root with
`list_allowed_directories` when available and verify the executor can access it. Keep final
deliverables at the requested path; inspect only relevant excerpts of verbose output while
preserving the exit code. Remove task-created scratch files when done.

The user's instructions take precedence. Follow repository instructions and use ordinary
permission-checked tools; this playbook grants no access. Report in the user's language.
Prefer a direct executable and argv array. Use a shell for its built-ins, pipelines, redirection
or scripts, and a dedicated tool when it better serves the task.

1. Establish the goal, inputs and authorized side effects. Check `process_run` availability,
   discovering it through `tool_search` if needed, and read its current description/schema.
   If unavailable, briefly explain and use another available executor; do not install/connect
   tools automatically. Identify the server OS, not the client's OS, and verify executable paths,
   shell availability/version and working directory with permitted read-only checks.
2. Use `executable`, string-array `arguments`, verified `workingDirectory` and supported
   `timeoutMs`. In the known contract, a bare executable name is resolved through PATH even with
   a working directory; use an absolute path or `./name` for a file in that directory. Each array
   element is one argv entry: do not wrap a path in extra quotes just because it contains spaces.
   There is no implicit shell, glob or variable expansion; `*`, `~`, `$VAR`, `%VAR%`, pipes and
   redirects are literal arguments unless a shell explicitly parses them. Stdin is closed at
   launch: use noninteractive modes and supported input files, not interactive prompts. Known
   parameters do not include stdin, environment, encoding, background mode or session polling;
   do not invent them. The actual schema wins.
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
   The working directory is not a sandbox. Verify full targets, empty inputs, source/destination
   overlap, existing outputs and symlinks/junctions. Before recursive deletion/moves confirm the
   final path is within the intended authorized area, not an accidental root, home or entire
   working tree. Preview targets or use dry-run/-WhatIf where supported; preserve important
   originals or write and verify a new output before replacement. Inspect the entire command:
   redirection can truncate a file before the program succeeds. Do not add blanket approval for
   ordinary reads or already authorized changes. Prepare concrete targets and ask only for missing
   authorization for irreversible actions outside the request. Declined stops that action;
   unresolved approval does not authorize it. Process termination does not undo partial changes.
6. Estimate duration: builds, data processing and automation may take hours. Check tool/client
   limits and whether a genuine managed long-running execution mechanism exists. The known
   `timeoutMs` range is 1–600000, default 600000. Omit it to use the effective tool policy;
   set a smaller value only for an intentionally shorter deadline. Timeout/cancellation terminates the process tree.
   Do not exceed the schema. If insufficient, use safely separable batches/stages with checkpoints,
   or an available managed job/session/queue with identifier, status, logs, cancellation and final
   result. Do not split atomic operations in ways that damage integrity. Change a timeout setting
   only when actually supported and authorized. If no suitable mechanism exists, explain the
   limit and prepare a reproducible script without claiming completion. Do not bypass limits with
   nohup, &, detached processes or Start-Process: children may be terminated and launch does not
   prove completion. When a supported workflow needs Start-Process on Windows, use -WindowStyle
   Hidden unless a visible interactive window is explicitly needed. After timeout inspect actual
   status, partial artifacts and logs before retrying safely resumable work.
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
   success. Known capture is limited to 32768 characters per stream; save complete authorized logs
   through program options or an explicit shell redirect with a correct encoding. Verify the final
   artifacts and intended postconditions. Before retrying failures inspect partial side effects.
   The final answer contains the requested command result or accessible artifacts, actual checks
   and any unresolved duration, encoding or completeness limits. A running job is not a passed run.
