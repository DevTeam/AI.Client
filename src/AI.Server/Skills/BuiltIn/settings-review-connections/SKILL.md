---
id: settings-review-connections
name: Settings review connections
icon: chart
kind: playbook
description: Compare Connections on small synthetic tasks and measured run times, report failures and recommend a default and subtask pool, then apply approved selections, deactivations or deletions.
parameters: {"type":"object","properties":{"connections":{"type":"array","items":{"type":"string"},"description":"Connection names or ids selected by the user"},"goal":{"type":"string","description":"The user's priority: quality, speed, cost or a particular kind of work"}},"additionalProperties":false}
tools: ["app_read","spawn_subtask","app_security","app_projects","app_chats","ask_user"]
---

1. Read all Settings pages with `app_read`. Match connections by exact name or id; ask about
   ambiguous names. Without a selection, offer enabled Connections in `ask_user`, multiSelect
   true, at most eight choices per question. Ask for the priority if unknown: "Quality
   (Recommended)", "Speed", "Cost", allowing another answer. Before any tests, explain the
   selected providers and bounded test size; they may consume paid tokens. Explicitly requested
   testing already authorizes this; otherwise ask "Run comparison (Recommended)" / "Only
   inspect settings". Dismissed, declined, expired or interrupted runs no tests and changes nothing.
2. Inspect each selected endpoint with `app_read` resource=ConnectionModels and its resourceId.
   Report whether its configured model is advertised. A catalog success is not proof of a
   working completion; a catalog failure or absent model is not proof that completions fail.
   Disabled entries can be inspected, but cannot be used by spawn_subtask; do not enable them
   silently. Leave them untested and tell the user they can enable them in Connections and
   request a comparison again.
3. Prepare the same small, self-contained synthetic task suite for every enabled candidate:
   one exact arithmetic/data transformation with a known answer, one short code reasoning
   problem with checkable edge cases, and one concise planning/instruction-following problem
   suited to goal. Decide the expected results and scoring rubric before running. Include the
   same output limit (for example 250 words) in every task. Send no project files, credentials,
   personal data or chat history. In each task instruct the subtask to answer directly, use no
   tools, spawn no children, and change no files or settings.
4. Call `spawn_subtask` with an explicit connectionId on EVERY task; never rely on the subtask
   pool or the current default. Keep batches at most eight tasks, leaving no nested subtasks.
   Start with one combined suite per candidate. Compare answers against the fixed rubric,
   completed/error, toolCalls, filesChanged and elapsedMilliseconds from the result. This is
   measured total run time, including application overhead, not token throughput or pure API
   latency. Report unexpected tool calls or writes and exclude those runs from speed comparison.
   Repeat a transient failure once, or close ties once with a second equivalent suite. Stop
   after two suites per candidate; ask before any more usage. Do not treat a timeout, auth
   error, rate limit or one bad answer as permanent failure.
5. Show a compact table: connection/model, completion status, checked quality, measured run
   time, known cost and error. Capability is a user rating from 1 to 5; use token prices or reported usage for cost. Do not infer prices,
   context size or capabilities from model names or elapsed time. Mark missing cost unknown
   and explain when a cost-efficiency recommendation cannot be made. Recommend an enabled
   default for goal and an optional subtask pool; several entries may have ForSubtasks true.
   State that a small sample is preliminary, with the concrete evidence and uncertainties.
6. Propose exact changes and call `ask_user`: default choice, subtask pool, and treatment of
   persistently failing entries. Offer "Keep unchanged (Recommended)", "Deactivate", "Delete"
   for failed entries, naming each affected connection. Deletion requires explicit selection;
   inspect Projects and their Chats first to report references and arrange approved replacement
   endpoints before deleting. For each reference, show the project/chat name and proposed
   replacement and include that change in the approval. Deactivation also needs a working replacement if the connection
   is referenced by a project or chat; do not silently leave them unusable. Dismissed, declined,
   expired or interrupted applies no changes. If the user already approved exact changes,
   do not ask again. Do not overwrite token prices or capability ratings without separate approval.
7. Before cleanup of referenced connections, read the affected Project/Chat again and use
   `app_projects` Update or `app_chats` SetEndpoint with its current revision and the explicitly
   approved enabled replacement connectionId. Use a fresh operationId for each change; on
   conflict re-read and retry once only if the approval still applies. Verify all references
   were replaced. If one fails, report partial changes and leave that connection present and
   enabled; do not claim the cleanup completed.
   Re-read Settings and apply only approved changes with `app_security` UpsertConnection for
   each changed entry, passing its current definition as expectedConnection. Use RemoveConnection
   with serverId and expectedConnection only for entries explicitly approved for deletion.
   Use a fresh operationId for each entry and re-read between dependent default changes.
   The Host keeps exactly one enabled default and clears IsDefault and ForSubtasks on disabled
   entries. Preserve a usable existing default when no replacement was approved. Leave all
   unrelated entries, MCP servers, environment metadata and policies untouched. On a conflict
   re-read and review the affected entry before retrying. Verify by reading Settings after saving.
8. The final answer contains the comparison table and recommendation, plus a short statement
   of the changes actually applied or that settings were kept. Never report a test not run as
   passed or expose ids, revisions or credentials.
