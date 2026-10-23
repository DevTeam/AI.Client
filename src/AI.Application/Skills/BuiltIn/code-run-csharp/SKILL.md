---
id: code-run-csharp
name: Code run C#
aliases: ["c#","cs","csharp","cs-run"]
icon: code-run-csharp
kind: playbook
description: Compute exact answers over many files or records with one C# script through cs_run - aggregate, reconcile or audit totals, parse CSV, JSON or logs, statistics and percentiles, find duplicates or differences, convert data, algorithms and exact arithmetic; changes files only within the user's authorized scope.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"The calculation or automation goal"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Input or output paths provided by the user"}},"additionalProperties":false}
tools: ["cs_run","list_allowed_directories","read_text_file","list_directory","write_file","process_run","ask_user"]
---

The user's instructions take precedence; this playbook grants no access. Report in the user's
language. A normal C# development request does not need a script; a single simple read or edit
belongs to the file tools.

1. Look at the input only as far as the script needs: a directory listing and the first lines of one
   file, or nothing when the task states the format. Do not read the data itself into the
   conversation.
2. Write one script that does the whole job: read every input, compute with `decimal` for money and
   exact rounding (`MidpointRounding.AwayFromZero` when asked for half away from zero), write the
   requested output, check it against its own invariants (counts, sums, ordering, the format asked
   for) and return a small object with the result and those checks as the last expression. An object
   or collection comes back as JSON; variables come back only as summaries, so return what you need.
   Use `CultureInfo.InvariantCulture` for numbers and dates in machine data and UTF-8 without BOM
   for new files. Pass user text through `arguments` or `globals`, not into the code.
3. When the result and its checks are right, answer. Do not run a second script that recomputes the
   same thing another way, or one that confirms the inputs did not change: the first script's own
   checks are the verification. Run another script only to fix what a check or a compiler message
   showed was wrong, and change only that.
4. Risks: a script that deletes, moves or overwrites must resolve and verify its targets inside the
   intended area first and keep important originals; ask only for authorization the request does
   not already give. A script always runs in a sandbox: it reaches only the granted directories and
   the chat's temporary directory, usually without the network, and runs without asking. An access
   error outside them is the boundary: ask the user through `ask_user` to grant that directory to
   the project and retry after the grant, never work around it, and use `fetch` or `process_run`
   for anything online.
5. Long work: keep `timeoutMs` at its default unless a shorter deadline is intended; split work
   that can outlast it into batches with checkpoints in the chat's temporary directory. Never stop
   a process this run did not start.
6. Answer with the result, where it was written and what the script checked. Do not accept a
   truncated or timed-out result as complete.
