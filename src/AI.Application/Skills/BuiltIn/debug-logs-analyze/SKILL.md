---
id: debug-logs-analyze
name: Debug logs analyze
icon: debug-logs-analyze
aliases: ["logs"]
kind: playbook
description: Analyze application or system logs over a bounded time window, including rolled and compressed files, against the host's actual current time; report the causal chain, gaps and whether the verbosity and retention were enough, and propose a targeted increase; changes nothing.
parameters: {"type":"object","properties":{"symptom":{"type":"string","description":"The failure, error text or behavior to explain"},"since":{"type":"string","description":"Start of the time window, absolute or relative (\"last hour\")"},"until":{"type":"string","description":"End of the time window"},"timeZone":{"type":"string","description":"The time zone the user means, when it differs from the host"},"correlationId":{"type":"string","description":"A request, trace, operation or user-visible error id to follow"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Log files or directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","zip_list","zip_read","process_run","cs_run","trigger_wait","ask_user","run_skill"]
---

For decompressed copies, extracts and intermediate counts, use the `purpose: "chatTemporary"`
root from `list_allowed_directories` when available. Never decompress, rename or rewrite a log
in place. Remove task-created scratch files when finished.

The user's instructions take precedence. Follow repository instructions, runbooks and the existing
toolchain. Use ordinary permission-checked tools; this playbook grants no access. Use `process_run`
with an executable and argument arrays, never interpolated shell commands. Logs routinely hold
secrets, tokens, connection strings, personal data and request bodies: quote only the lines the
argument needs, redact those values in every excerpt, and never send log content to an external
service. Report in the user's language.

Keep this skill read-only: do not edit logging configuration, delete, truncate, move or rotate
log files, restart services or clear caches. A log file is often held open by the process writing
it; read it as it is and never stop, kill or restart a process this run did not start to free it.

Read large or many logs with one `cs_run` script that filters the window, counts by level and
source, finds the first and last occurrence of each signature and returns a compact JSON summary
with file and line references, instead of paging through them with read calls.

1. Establish "now" from the host, never from your own sense of the date. Call `process_run` once
   for the clock: on Windows `powershell` with `-NoProfile -Command [DateTimeOffset]::Now.ToString('o')`
   and `[TimeZoneInfo]::Local.Id`; elsewhere `date` with `+%Y-%m-%dT%H:%M:%S%z` and `+%Z`. Record
   local time, UTC offset and zone. When the logs come from a container, remote host or
   provider, read that source's clock or zone through a permitted read-only query as well; never
   assume it matches the host. Resolve the parameters and relative phrases ("last hour",
   "this morning", "yesterday's deploy") against this time in the user's zone (`timeZone`
   when given), and write the resulting window with explicit offsets. A window that ends in the
   future or starts before the oldest surviving entry is reported, not silently clipped.
2. Find the logs. Use `paths` when given; otherwise search before reading: logging
   configuration (appsettings*.json, Serilog/NLog/log4net/logback/log4j2 files, Python/Node logging
   setup, docker-compose `logging`, systemd units), runbooks and the conventional directories
   (`logs/`, `%LOCALAPPDATA%`, `/var/log`, the application data directory). The configured path and
   file-name template are the evidence; a file merely called `*.log` may be stale. Use `ask_user`
   with `pathKind: "file"` or `pathKind: "directories"`, `options: []` and `allowOther: false` only
   when the location cannot be inferred; read a picked file from `answers[].other` and directories
   from `answers[].paths`. For journald, Windows Event Log, `docker logs`, `kubectl logs` or a
   provider's log query, use the existing CLI through `process_run` with an explicit bounded time
   range (`--since`/`--until`, `-StartTime`, a line cap) and the actual target context.
3. Reconstruct the rolling set before reading. List every sibling of the active file that the
   configured template produces: size-numbered (`app.log.1`, `app_001.log`), date-stamped
   (`app-20261013.log`, `app.log.2026-10-13`), compressed (`.gz`, `.zip`) and archive
   directories. Record name, size and modification time with `get_file_info`, then order
   the set by the first and last timestamps inside each file, not by modification time or by name:
   numbered schemes renumber on every roll, and copied files keep new times. Read `.zip` archives
   with `zip_list`/`zip_read`; decompress `.gz` and other formats into scratch with an existing
   tool through `process_run`. Note the configured rolling policy (interval, size limit,
   retained-file count, total size cap, `maxHistory`) and whether it already discarded part
   of the window. An entry split across a roll boundary belongs to the earlier file.
4. Check freshness against "now". Compare the newest entry of the active file with the current
   time and the expected write rate. A log that stops before the symptom means the writer stopped,
   crashed, writes elsewhere, buffers, or lost its file to an external rotation without reopening
   it; that is a finding in itself. For a live symptom, one bounded `trigger_wait` on the active
   file can confirm new entries arrive instead of repeated polling; it ends with this chat run.
5. Normalize time. Determine each source's timestamp format and zone: an explicit offset, `Z`,
   a configured format, or none. Entries without an offset are local to the process that wrote
   them, which inside a container is usually UTC; state the assumption. Watch for DST shifts,
   12-hour clocks, missing dates on rolled lines and clock skew between machines (compare the same
   request's entries on both sides). Convert everything to one zone before correlating.
6. Read bounded. Never load a whole large file: `grep_files` for the window's timestamps, the
   symptom text, the `correlationId`, exception type names and level markers, then read the
   surrounding lines with `read_text_file` `head`/`tail` or extracted ranges. Count entries per
   level and per minute over the window, and compare with an equally long healthy window before it
   to see what changed (new message templates, a rate spike, a sudden silence). Collapse repeated
   lines into one with a count and the first and last time.
7. Follow the causal chain. Start from the first anomaly, not the loudest error: the earliest
   warning, retry, timeout, slow call or configuration line that precedes the symptom.
   Follow request/trace/correlation ids across files and services. Separate the cause from its
   echoes (retries, cascading timeouts, health-check failures). Stack traces found in the logs can
   go to debug-stacktrace-analyze and trace files to debug-trace-analyze through `run_skill` when
   the user wants them examined in depth. Test one hypothesis at a time; a single message is not a
   definitive cause.
8. Assess whether the logs were enough. For the window, state the levels actually written
   per relevant category (for example only Warning and Error from `MyApp.Data`), the effective
   minimum level from configuration, including per-category overrides and environment overrides,
   and what the analysis could not establish because of it: missing request boundaries,
   no timing, no parameters, no correlation id, swallowed exceptions, a sampled or rate-limited sink.
   Also state whether retention covered the window and how long the current policy keeps
   data at the observed write rate. When the evidence is insufficient, propose the smallest change
   that would close the gap: the exact category or namespace, the level (Information, then Debug;
   Trace only for a short reproduction), the duration, the expected volume increase and its effect
   on rolling (a faster roll at a fixed retained-file count evicts older evidence, so raise the count
   or size cap with it), the performance cost, the extra sensitive data a lower level may write, and
   how to revert. Do not raise the level of everything.
9. When the user wants the proposed logging change applied, run debug-logging-configure through
   `run_skill` with the proposal; do not edit configuration here. Otherwise ask nothing further.
10. Report in a few sections: the resolved window with offsets and the host's current time; the
   sources and rolled files read, with gaps; the timeline of relevant entries (redacted, with
   file and line or timestamp references); the causal chain or ranked hypotheses with the evidence
   that supports or refutes each; the verbosity and retention verdict; the proposed logging change,
   if any; and the next discriminating check.
