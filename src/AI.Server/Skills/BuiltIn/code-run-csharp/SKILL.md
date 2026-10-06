---
id: code-run-csharp
name: Code run C#
aliases: ["c#","cs","csharp","cs-run"]
icon: code-run-csharp
kind: playbook
description: Solve algorithmic, computational and complex automation tasks with C# through cs_run when available; may change files or external data only within the user's authorized scope, with risk, duration and encoding checks.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"The calculation or automation goal"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Input or output paths provided by the user"}},"additionalProperties":false}
tools: ["tool_search","cs_run","process_run","read_text_file","get_file_info","list_directory","write_file","create_directory","ask_user"]
---

The user's instructions take precedence. Follow repository instructions and use ordinary
permission-checked tools; this playbook grants no access. Report in the user's language.
Use C# for algorithms, graph search, optimization, exact arithmetic, simulations, hypothesis
checks and complex data transformations when a script improves reliability. Prefer a dedicated
tool for simple operations. A normal C# development request does not require executing a script.

1. Establish the goal, input, output and authorized changes from the message and parameters.
   Check available tools; discover `cs_run` through `tool_search` when necessary. It is an optional
   external tool and may not be installed or connected. Source files or a .NET runtime do not prove
   availability. Read the actual tool description and schema, including its host OS and limits.
   If unavailable, explain briefly and continue with a suitable available tool or runtime through
   `process_run`; do not install or connect a server automatically. Report a concrete blocker if
   no available mechanism can complete the task.
2. Select an algorithm and estimate time, memory and output size. Pass data through `arguments`
   (`Args`) or `globals` (`Globals`, a read-only dictionary of `JsonElement`), rather than injecting
   user text into source code. Use top-level C# statements and optional `using` directives; no
   `Program` or `Main` is needed. A final expression without a semicolon is the return value.
   Each call is independent. Use `BigInteger` for exact large integers, `decimal` when exact decimal
   arithmetic is needed, explicit tolerances for numerical methods and a seed for reproducible
   simulations. Additional `imports` are namespaces; `references` are resolvable assembly names or
   DLL paths, not NuGet package names. Never assume automatic package installation.
3. Assess real risks before execution: deletion, overwrite, moves, database/network mutations,
   resource exhaustion and side effects of invoked programs. `cs_run` runs in the server process,
   not a sandbox; its working directory does not restrict access. Client files may not exist on
   the server. Resolve and verify full source/destination paths, empty inputs, existing outputs,
   symlinks/junctions and source/destination overlap. Before recursive deletion or moves verify
   the final target is inside the intended authorized area, not an accidental root, home or whole
   working tree. Preview targets or use a dry run where supported. Preserve important originals
   or write and verify a separate output before replacement. For read-only calculations, check
   resource bounds without introducing a blanket approval step. Existing authorization stands;
   prepare concrete targets and ask only for missing authorization for irreversible actions
   outside the request. Declined stops that action; unresolved approval never authorizes it.
4. Plan duration before starting. Work can take hours and exceed both tool and client timeouts.
   The known cs_run contract accepts `timeoutMs` from 1 to 600000, default 600000; the current
   schema wins. Omit it to use the effective tool policy; set a smaller value only for an
   intentionally shorter deadline. Never pass a value above its maximum. For long work, use bounded batches with
   persisted checkpoints and resumable progress, or a genuinely available managed long-running
   job/session with status, logs, cancellation and a final result. Moving to `process_run` alone
   does not remove its timeout. If no suitable mechanism exists, explain the limit and prepare a
   reproducible script without claiming it ran. Do not leave background Tasks in the server or
   bypass timeouts. Bound loops with a local deadline (for example `Stopwatch`) and pass local
   cancellation tokens to cooperative I/O. Arbitrary C# is not guaranteed to stop on timeout;
   use a controlled separate process if hard termination or isolation is required. Check actual
   state before retrying timed-out work, especially mutations.
5. Preserve encoding. JSON code, Args and Globals are Unicode strings; ordinary Console output
   is captured as text. Changing the server's global Console encoding is unnecessary and does not
   repair incorrectly decoded input. For byte streams and files choose an explicit encoding;
   default new text artifacts to UTF-8 without BOM unless the consumer requires another format.
   Respect existing BOM, encoding and line endings; missing BOM does not establish UTF-8. Use
   strict decoding when corruption matters rather than silently persisting replacement characters.
   For child programs separately agree stdin/stdout/stderr encodings before launch. Distinguish
   encoding from culture; use explicit numeric/date formats or InvariantCulture for machine data.
   Check a short non-ASCII round trip when its preservation is important. Avoid logging secrets.
6. Run the self-contained script with a verified server-side absolute `workingDirectory` when
   needed. In the known implementation, a missing directory can leave the previous directory
   unchanged, so verify existence. Return compact structured data with `JsonSerializer.Serialize`:
   `returnValue` and `variables` are textual renderings, not automatically JSON objects. Known
   limits are 262144 code characters, 32768 characters per output stream, 4096 per value and 64
   returned variables. Save large authorized results to a file, verify it and ensure the user can
   access it if the server is remote. Avoid modifying server global state, Environment.Exit and
   unjoined background work. Run operations sharing process state sequentially.
7. Inspect `success`, `diagnostics`, `error`, `timedOut`, `truncated`, stdout and stderr. Fix
   compilation problems from diagnostics. Before retrying runtime failures inspect partial side
   effects: restoration of working directory/environment/console does not roll back files or
   remote data. Validate a known small case, invariant, independent calculation or error bound;
   for batches verify the full expected range was completed. Do not accept a truncated answer as
   complete. The final answer contains the computed result, verification and material accuracy or
   completion limits; for large artifacts include their accessible location and a useful summary.
