---
id: debug-dump-analyze
name: Debug dump analyze
icon: debug-dump-analyze
aliases: ["dump"]
kind: playbook
description: Analyze a crash, hang or memory dump (Windows minidump, .NET dump, Linux core file, Java heap or thread dump) with installed debuggers to find the faulting thread, deadlock or leak; capturing a new dump of a live process needs explicit confirmation; changes nothing else.
parameters: {"type":"object","properties":{"symptom":{"type":"string","description":"Crash, hang, high memory or high CPU, as observed"},"version":{"type":"string","description":"The build or version of the program that was dumped"},"processId":{"type":"integer","description":"A live process to capture, only when the user asks for a capture"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Dump files, symbol or binary directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","process_run","ask_user","run_skill"]
---

For debugger output, extracted reports and a requested new dump, use the `purpose: "chatTemporary"`
root from `list_allowed_directories` when available. Remove task-created scratch files when
finished, except a dump the user wants to keep.

The user's instructions take precedence. Follow repository instructions and the existing
toolchain. Use ordinary permission-checked tools; this playbook grants no access. Use `process_run`
with an executable and argument arrays, never interpolated shell commands. A dump is a copy of the
process memory: it holds secrets, keys, tokens, request bodies and personal data. Never upload it,
attach it to a message or send it to a symbol or analysis service other than the configured symbol
servers; quote only the values the argument needs, redacted. Report in the user's language.

Keep this skill read-only: do not edit source or configuration, install debuggers or extensions,
or change symbol settings globally. Never stop, kill or restart a process. Capturing a dump of a
live process is done only when the user asks for it: name the process, its PID, the dump type
(mini, heap, full), the expected size and that the process is suspended while it is written, in
`ask_user`, and act only on an explicit affirmative answer; a declined, dismissed, expired or
interrupted answer captures nothing. Use a capture mode that does not terminate the process.

1. Find the dump from `paths` or the message; otherwise search before reading the usual places
   (crash dump folders, `%LOCALAPPDATA%\CrashDumps`, WER report folders, `core` files and the
   `core_pattern` target, systemd-coredump, the application's dump directory). Use `ask_user` with
   `pathKind: "file"`, `options: []` and `allowOther: false` only when it cannot be found. Record its
   size and time with `get_file_info`.
2. Establish "now" from the host clock with one `process_run` call (on Windows `powershell` with
   `-NoProfile -Command [DateTimeOffset]::Now.ToString('o')`; elsewhere `date` with
   `+%Y-%m-%dT%H:%M:%S%z`), never from your own sense of the date, and compare it with the dump's
   time. State whether the dump belongs to the incident being discussed and whether the binaries
   deployed now are still the ones that were dumped.
3. Identify the dump kind (crash with an exception record, hang/manual, heap) and runtime, and
   pick an installed tool: `dotnet-dump analyze` for .NET (`clrthreads`, `pe`, `clrstack -all`,
   `dumpheap -stat`, `gcroot`, `syncblk`), `cdb`/WinDbg with `!analyze -v` for Windows native
   and `.loadby sos coreclr` for managed, `lldb`/`gdb` with `bt all` for core files, `jhsdb`,
   `jstack` output or an installed heap analyzer for Java. Run them non-interactively with a
   command list and an explicit finite timeout, writing output to scratch. A missing tool leaves the
   analysis blocked: name the tool and its install command instead of installing it.
4. Get symbols right before reading stacks. Use the exact binaries and PDB/DWARF files of the
   dumped version (`version`, module versions in the dump, the build output), from paths in
   `paths` or the configured symbol path. Mismatched or missing symbols give plausible but wrong
   frames: check module versions and timestamps, and report frames without symbols as unresolved.
5. Analyze by symptom.
   - Crash: the faulting thread, exception code and record, the managed or native exception and its
     inner ones, the deepest application frame; hand the stack to debug-stacktrace-analyze through
     `run_skill` when source-level reasoning is needed.
   - Hang: all thread stacks grouped by identical stack, lock owners and waiters (`syncblk`, critical
     sections, monitors, `!locks`), cycles that form a deadlock, threads blocked on I/O, the
     thread pool and timers; a starved thread pool looks like a hang without any deadlock.
   - Memory: heap statistics by type and size, growth suspects, `gcroot` paths of a sample of
     the suspect objects, large object heap, pinned handles, native versus managed memory, finalizer
     queue. One dump shows what is there, not what grows: say so, and propose a second dump
     some time later to diff.
   - CPU: stacks of the busiest threads; one dump is a single sample, so propose a trace with
     debug-trace-analyze for a confident answer.
6. Correlate with the logs and traces around the dump time when they exist (debug-logs-analyze
   through `run_skill`), and mark what the dump shows and what is inferred.
7. Assess whether the dump was enough: a minidump without heap cannot answer memory questions, a
   dump taken after recovery shows nothing, missing symbols hide frames. Propose the right capture
   for next time (type, trigger such as an exception or a memory threshold, the tool, where it is
   written and the disk space it needs, and the data sensitivity of a full dump).
8. When the user asks for a new capture of `processId` or a named process, prepare the exact
   command, then confirm it with `ask_user` as described above; after an affirmative answer run it
   once and analyze the result.
9. Report the dump (kind, time against now, version, symbols state), the faulting thread, deadlock
   or leak with the commands and output excerpts that show it, the cause or ranked hypotheses, the
   gaps, and the recommended fix or next capture.
