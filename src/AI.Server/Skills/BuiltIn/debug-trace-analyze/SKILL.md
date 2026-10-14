---
id: debug-trace-analyze
name: Debug trace analyze
icon: debug-trace-analyze
aliases: ["trace"]
kind: playbook
description: Analyze distributed traces, spans and runtime or performance traces (OpenTelemetry, Jaeger/Zipkin JSON, .nettrace, ETW, perf, strace, browser HAR or performance profiles) to find the critical path, the failing or slow span and its cause; changes nothing.
parameters: {"type":"object","properties":{"symptom":{"type":"string","description":"The slow, failing or unexpected operation"},"traceId":{"type":"string","description":"A trace, operation or request id to follow"},"since":{"type":"string","description":"Start of the time window, absolute or relative"},"until":{"type":"string","description":"End of the time window"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Trace files or directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","zip_list","zip_read","process_run","ask_user","run_skill"]
---

For converted traces, extracts and reports, use the `purpose: "chatTemporary"` root from
`list_allowed_directories` when available. Never rewrite an original trace. Remove task-created
scratch files when finished.

The user's instructions take precedence. Follow repository instructions, runbooks and the existing
toolchain. Use ordinary permission-checked tools; this playbook grants no access. Use `process_run`
with an executable and argument arrays, never interpolated shell commands. Span attributes, HTTP
headers, URLs, SQL text and HAR bodies often hold credentials, cookies and personal data: redact
them in every excerpt and never upload a trace to an external viewer or service. Report in the
user's language.

Keep this skill read-only: do not change sampling or exporter configuration, restart services or
install tools. Collecting a new trace attaches to a running process and slows it; do it only when
the user asks for a capture, name the exact process or PID and the duration in `ask_user`, and act
only on an explicit affirmative answer. Never stop, kill or restart a process this run did not start.

1. Establish "now" from the host clock with one `process_run` call (on Windows `powershell` with
   `-NoProfile -Command [DateTimeOffset]::Now.ToString('o')`; elsewhere `date` with
   `+%Y-%m-%dT%H:%M:%S%z`), never from your own sense of the date. Resolve relative windows against
   it with explicit offsets. Trace timestamps are usually epoch micro- or nanoseconds in UTC, while
   logs are often local: convert before correlating, and note clock skew between services, which
   shows as a child span starting before its parent or ending after it.
2. Identify the trace format and source from `paths`, configuration (OpenTelemetry exporter
   and sampler settings, collector configuration, APM agent files) or runbooks. Search before
   reading. Use `ask_user` with `pathKind: "file"` or `pathKind: "directories"`, `options: []`
   and `allowOther: false` only when the source cannot be inferred. For a tracing backend, query it
   through its existing CLI or API via `process_run` with the exact `traceId` or a bounded time
   range and service filter; do not page through everything.
3. Read the trace with an appropriate tool. JSON exports (OTLP, Jaeger, Zipkin, Chrome trace
   events, HAR) are read in bounded parts with `grep_files` and `read_text_file`; large ones are
   summarized through an existing tool such as `jq` into scratch. Binary formats need their own
   tool: `dotnet-trace convert`/`dotnet-counters` for .nettrace, `perf script`/`perf report`,
   `wpaexporter`/`tracerpt` for ETL, `strace -c` summaries. Use only tools already installed;
   a missing tool leaves that part unverified and the report names the tool that would read it.
4. Check completeness before judging. Sampling (head, tail or rate based) may have dropped the
   interesting traces; missing parents, orphan spans, spans cut at an exporter batch boundary and
   services without instrumentation all leave gaps. Record the sampler and what is not visible.
5. Build the critical path: the chain of spans that determines the end-to-end duration, with
   self time per span (duration minus children overlapping on the path). Find the first span with
   an error status or exception event, and the span whose self time dominates. Distinguish waiting
   (queues, locks, connection pools, retries with back-off, DNS/TLS setup) from work, fan-out that
   ran serially, N+1 patterns of repeated identical child spans, and timeouts whose duration equals
   a configured limit.
6. Compare with a healthy trace of the same operation when one is available, and with the
   relevant logs by trace or span id. A slow span is a location, not yet a cause: name what in it
   took the time, with evidence. Stack traces from exception events can go to
   debug-stacktrace-analyze and the surrounding logs to debug-logs-analyze through `run_skill`.
7. Assess whether the instrumentation was enough: which hops have no spans, which spans lack the
   attributes that would explain them (query, peer, retry count, payload size), and whether the
   sampler kept errors. Propose the smallest change that would close the gap (a manual span, an
   attribute, an error-keeping sampler, a temporary higher sample rate for one service) with its
   overhead and data-sensitivity impact. Applying it is a separate change: hand logging-level changes
   to debug-logging-configure through `run_skill` and code changes to the matching code skill only
   when the user asks.
8. Report the trace(s) and window with offsets, the critical path with durations, the failing or
   dominant span and the supported cause or ranked hypotheses, the gaps in the data, the proposed
   instrumentation change if any, and the next discriminating check.
