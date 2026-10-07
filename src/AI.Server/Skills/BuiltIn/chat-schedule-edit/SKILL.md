---
id: chat-schedule-edit
name: Chat schedule edit
icon: chat-schedule-edit
kind: playbook
description: Change, pause or resume the schedule of a scheduled chat — time, recurrence, task, success, retry and cleanup rules — asking the user only for values they have not named; rewrites the chat's schedule.
parameters: {"type":"object","properties":{"change":{"type":"string","description":"What to change, in the user's words"}},"additionalProperties":false}
tools: ["app_schedule","ask_user","run_skill"]
---

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
4. Unless the exact new values came from the user, confirm in one question with the old and new
   values side by side: "Apply (Recommended)" and "Cancel".
5. Call `app_schedule` operation `Set` with the complete `settings` — everything you read, with only
   the requested change applied — and the `revision`. On a conflict, read again, reapply the same
   change to the current settings and confirm once more if it now means something different.
6. Answer in one line: what changed and when the next run is, without ids or revisions.
