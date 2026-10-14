---
id: debug-stacktrace-analyze
name: Debug stacktrace analyze
icon: debug-stacktrace-analyze
aliases: ["stacktrace"]
kind: playbook
description: Analyze a stack trace or exception report from any runtime, map its frames to the source at the revision that actually ran, and explain the failing path and root cause with a suggested fix; changes nothing.
parameters: {"type":"object","properties":{"stacktrace":{"type":"string","description":"The stack trace or exception text as pasted"},"version":{"type":"string","description":"The build, version or commit that produced it"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files holding the trace, or the source directories, explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","process_run","ask_user","run_skill"]
---

The user's instructions take precedence. Follow repository instructions and the existing
toolchain. Use ordinary permission-checked tools; this playbook grants no access. Use `process_run`
with an executable and argument arrays, never interpolated shell commands. Exception messages
often carry connection strings, tokens, file paths with user names and personal data: redact them
when quoting. Report in the user's language.

Keep this skill read-only: do not edit source, check out another revision in the working tree,
install symbol tools or upload a trace to an external service.

1. Take the trace from `stacktrace`, the message, or a file in `paths`. When it is cut, wrapped or
   interleaved with other log lines, reassemble it and say what is missing. Search before reading
   for the full version in logs when only a fragment was given. Use `ask_user` with
   `pathKind: "file"`, `options: []` and `allowOther: false` only when the trace or its source
   cannot be found otherwise.
2. Identify the runtime and its conventions: .NET (`---> ` inner exceptions, `--- End of inner
   exception stack trace ---`, async state machine frames such as `MoveNext()` and
   `<Method>d__12`, `AggregateException` with several inner ones), Java/Kotlin (`Caused by:`,
   `... N more`, `Suppressed:`), Python (most recent call last, `During handling of the above
   exception`), JavaScript (minified frames that need a source map), Go (goroutine dumps, `panic:`),
   native (addresses, module+offset, that need symbols). Order the exceptions from the root
   (innermost or last `Caused by`) to the outermost, because the root is usually the cause and the
   outer ones are wrappers.
3. Establish the revision that produced the trace: `version`, assembly/package versions or a
   commit hash in the trace or its log line, release tags. Line numbers only mean something for that
   revision. Read that revision's source without checking it out (`git show <rev>:<path>`,
   `git log`/`git blame` on the range) and resolve the reference with
   `git rev-parse --verify --end-of-options`. When the revision cannot be determined, say the line
   mapping is approximate and match frames by method instead. When the trace has a timestamp, take
   "now" from the host clock with one `process_run` call (on Windows `powershell` with
   `-NoProfile -Command [DateTimeOffset]::Now.ToString('o')`; elsewhere `date` with
   `+%Y-%m-%dT%H:%M:%S%z`), never from your own sense of the date, and say how old the trace is and
   whether a deployment or commit since then may already have changed the failing code.
4. Map the frames to source. Separate the application's own frames from framework, library and
   generated ones; the most informative frame is usually the deepest application frame, not the
   top one. Use existing tools only: source maps through an installed tool, `addr2line`/`dotnet-symbol`
   or the platform symbolizer for native frames when symbols are available. Missing symbols leave
   frames unresolved; report them as such and never invent a method name.
5. Explain the failure: the state that made the throwing line fail (null source, empty collection,
   out-of-range index, closed connection, cancelled token, timeout, permission, concurrency on a
   non-thread-safe object, configuration value), where that state came from along the call path,
   and why the existing guards did not catch it. Read the callers and data flow with `grep_files`
   and `read_text_file`. Mark which part is shown by the code and which is inferred. A trace without
   the triggering input often fits several causes: rank them and name the evidence that would
   decide.
6. Check whether the trace is complete enough. A swallowed or rethrown exception (`throw ex`,
   wrapping without the inner exception, logging only `ex.Message`), stripped async frames, release
   builds without line numbers or a filter that drops the inner exceptions all hide the cause.
   Propose the concrete change that would preserve it next time (log the full exception, keep the
   inner one, ship portable PDBs or source maps, raise one category's level); hand a logging-level
   change to debug-logging-configure through `run_skill` only when the user asks.
7. Suggest a fix at the cause rather than a catch at the symptom, and say which test would
   reproduce it. Implementing it is code-bug-fix through `run_skill` when the user asks; this skill
   does not edit files.
8. Report the runtime and revision, the exception chain from root to outermost, the key frames
   with source references, the explanation with its evidence, the gaps in the trace and the
   suggested fix and test.
