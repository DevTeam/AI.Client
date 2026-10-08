---
id: chat-schedule-edit
name: Chat schedule edit
icon: chat-schedule-edit
kind: playbook
description: Change, pause or resume the schedule of a scheduled chat — time, recurrence, task, success, retry and cleanup rules — asking the user only for values they have not named, and allowing the tools and directories a changed task needs so a run never waits for a person; rewrites the chat's schedule.
parameters: {"type":"object","properties":{"change":{"type":"string","description":"What to change, in the user's words"}},"additionalProperties":false}
tools: ["app_read","app_schedule","app_security","ask_user","run_skill"]
---

Nobody is watching a run: a tool call whose effective policy is `Ask`, or a path outside the
project's directory grants, leaves the run `Blocked`, waiting for a person who may not be there.
Changing what a run does therefore also means checking that the run can still do it alone.

1. Take `projectId` and `chatId` from output.context, unless the user named another chat. Call
   `app_schedule` operation `Get`. Keep the schedule `revision` and the whole `settings`. Without a
   schedule, offer `chat-schedule-create` with `run_skill` instead. Resolve relative dates against the
   returned `localNow`.
2. Work out exactly what the user wants changed from `change` and the conversation. Pausing and
   resuming are their own operations: call `app_schedule` `Pause` or `Resume` with the revision,
   report in one line and stop.
3. For every value the change needs and the user did not give, ask in one `ask_user` call, in the
   user's language: a new recurrence with `pickerKind: "recurrence"`, a date with `"date"`, a time
   with `"time"`, and presets as options carrying an exact `value`. Show the current value in the
   question text so the user sees what changes. Never ask about a value they already named, and
   never choose one for them. A dismissed, expired, declined or interrupted question changes
   nothing; say so and stop.
4. When the change touches what a run does — the task, its inputs or its success criteria — redo the
   readiness check for the new task before confirming: read `app_read` resources Project, Chat and
   Settings, and each server in play through `app_read` resource=McpTools for the exact
   `(serverId, name, schemaHash)` of every tool the run will call. Work out each effective decision
   (chat overrides project overrides global; a disabled or denied server denies its tools; unknown
   means `Ask`). Only `Allow` runs with nobody there. For a needed tool left at `Ask`, ask once in
   `ask_user` — what it does and why the run needs it — with "Allow it for this chat (Recommended)"
   and "Keep asking me", and apply the approved ones with `app_security` operation
   `SetChatToolPolicy`, the chat's id, a fresh `operationId` per distinct change and the identity,
   `Allow` and limits (128 calls and 120 s for a bounded read, 32 for a write, 8 for something
   destructive, 600 s for `process_run`, `cs_run` and `trigger_wait`). Never grant `Allow` to a
   tool that manages its own permissions, such as `app_security`. For a missing directory or a
   missing `write`/`edit` capability, ask with `pathKind: "directories"` and "Read only
   (Recommended)" or "Read and write", then add each with `app_security` AddDirectoryGrant. When
   the change only moves a time, a retry, a cleanup rule or a pause, skip this step.
5. Unless the exact new values came from the user, confirm in one question with the old and new
   values side by side, naming the tools and directories allowed for the run: "Apply (Recommended)"
   and "Cancel".
6. Call `app_schedule` operation `Set` with the complete `settings` — everything you read, with only
   the requested change applied — and the `revision`. On a conflict, read again, reapply the same
   change to the current settings and confirm once more if it now means something different.
7. Answer in one line: what changed, when the next run is, and anything a run still has to wait for,
   without ids or revisions.
