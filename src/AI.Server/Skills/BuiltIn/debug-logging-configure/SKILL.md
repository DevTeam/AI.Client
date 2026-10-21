---
id: debug-logging-configure
name: Debug logging configure
icon: debug-logging-configure
aliases: ["log-level"]
kind: playbook
description: Review the effective log levels, sinks and rolling/retention policy and, after confirmation, raise or restore verbosity for specific categories and adjust retention for a diagnosis, recording how to revert; never restarts a process it did not start.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"What the logs must show, or the proposal from a log analysis"},"categories":{"type":"array","items":{"type":"string"},"maxItems":50,"description":"Logger categories, namespaces or modules to change"},"level":{"type":"string","description":"The requested level, when the user named one"},"duration":{"type":"string","description":"How long the raised level should stay"},"restore":{"type":"boolean","description":"Restore the levels recorded before an earlier raise"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Logging configuration files or directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","edit_file","process_run","trigger_wait","ask_user"]
---

The user's instructions take precedence. Follow repository instructions, runbooks and the existing
logging framework. Use ordinary permission-checked tools; this playbook grants no access. Use
`process_run` with an executable and argument arrays, never interpolated shell commands. Do not
write secret values into configuration or the answer. Report in the user's language.

1. Establish "now" from the host clock with one `process_run` call (on Windows `powershell` with
   `-NoProfile -Command [DateTimeOffset]::Now.ToString('o')`; elsewhere `date` with
   `+%Y-%m-%dT%H:%M:%S%z`), never from your own sense of the date. A raised level is recorded
   with this time and the agreed end time, both with offsets.
2. Find the configuration from `paths` or by searching before reading: appsettings*.json and
   environment-specific variants, `Logging`/`Serilog` sections, nlog.config, log4net/logback/
   log4j2 files, Python `logging` dictConfig or ini files, Node logger setup, environment variables
   (`Logging__LogLevel__*`, `Serilog__MinimumLevel__*`, `LOG_LEVEL`), container and orchestrator
   manifests, systemd units. Use `ask_user` with `pathKind: "file"` or `pathKind: "directories"`,
   `options: []` and `allowOther: false` only when the scope cannot be inferred. Record
   `git status --porcelain=v1 -uall` when applicable and preserve existing changes.
3. Work out the effective configuration for the environment that matters, not the first file
   found: the precedence of base file, environment file, environment variables, command line and
   code (`SetMinimumLevel`, filters, `MinimumLevel.Override`), the default and per-category
   minimum levels, per-sink minimum levels (a sink can filter what the logger emits), sampling or
   rate limits, and whether the framework reloads configuration on change (`reloadOnChange`,
   `autoReload`, `scan`) or needs a restart. Read the rolling policy: interval, size limit,
   retained-file count, total size cap, compression, and the free space of the target volume
   (`get_file_info` and a read-only `process_run` query).
4. Measure the current volume: entries or bytes per hour per level from the active and the most
   recent rolled files (sizes and timestamps; a bounded `grep_files` count). From it estimate how long
   the current retention keeps data and how a raised level changes that. When `restore` is set,
   find the record of the earlier raise (the comment or note left in step 7, `git diff`, the
   user's message) and go to step 6 with the original values.
5. Design the smallest change that answers `goal`: only the listed or evidently relevant
   categories; Information before Debug, Trace only for a short reproduction; never a global
   Debug or Trace in production unless the user explicitly names it. Raise the retained-file count
   or size cap together with the level when the faster roll would evict the window under
   investigation, within the free space. Note the extra sensitive data a lower level may write
   (request bodies, SQL parameters, headers) and the performance cost. Prefer the environment-
   specific file or an environment override over the shared base file so the change does not ship
   to every environment.
6. Confirm with `ask_user` (answerStyle='buttons'): show the exact files and keys with old and new values, the reload or
   restart requirement, the expected volume and retention, the sensitive-data note and the revert
   step. Options: "Apply (Recommended)", "Apply without retention change" when retention changes,
   "Cancel". A value the user already gave exactly is not asked again. A declined, dismissed,
   expired or interrupted answer changes nothing and the report lists the proposed change.
7. Apply with `edit_file` as the smallest edit that keeps the file's formatting, comments and
   order. Where the format allows comments, leave one next to each changed key with the original
   value and the agreed end time so the change can be found and reverted; otherwise put both in the
   report. Validate the file (JSON/XML parses, the framework's own check when one exists) and show
   `git diff` of exactly these lines.
8. Make it take effect. When the framework reloads on change, one bounded `trigger_wait` on the
   active log file can confirm that entries at the new level appear. When a restart is needed,
   never stop, kill or restart a process this run did not start: say which process needs it and
   that the user restarts it. A process this run started may be restarted only after `ask_user` (answerStyle='buttons')
   names it or its PID and the answer is an explicit yes.
9. A raised level is temporary. Remind the user of the end time and offer to restore it with this
   skill (`restore`); do not leave Debug or Trace active silently. Do not commit or push.
10. Report the effective configuration before and after, the files changed, whether it is active
   yet, the expected volume and retention, the revert step and the end time.
