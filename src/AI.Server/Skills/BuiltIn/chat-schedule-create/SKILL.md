---
id: chat-schedule-create
name: Chat schedule create
icon: chat-schedule-create
aliases: ["schedule"]
kind: playbook
description: Set up a task to run later or repeatedly — once at a date and time, every day, every weekday, every Monday, hourly or monthly — by turning this chat, or a new chat, into a scheduled chat instead of doing the task now; asks the user for every missing time, success, retry and cleanup rule, changes the chat's kind and starts automatic runs.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"What each run should do, if the user said it"},"when":{"type":"string","description":"When it should run, in the user's words"},"newChat":{"type":"boolean","description":"True when the user wants a separate new chat instead of this one"}},"additionalProperties":false}
tools: ["app_read","app_schedule","app_chats","ask_user","app_navigate"]
---

Do not carry out the task in this turn: the user asked for it to run on a schedule, and this skill
only sets the schedule up. The first run does the work.

A scheduled chat keeps its history. At every occurrence the application forks it from the end of
its main branch, and that run branch carries out the task with the `chat-schedule-run` skill. Never
invent a value the user did not give and never fill one in silently: every missing value is asked
with `ask_user`. Ask only for what is missing; a value the user already named in this chat, in the
branch or in the arguments is never asked again.

1. Take `projectId`, `chatId` and `branchId` from output.context. Call `app_schedule` operation
   `Get` for this chat. If it already has a schedule, follow `chat-schedule-edit` instead.
   Read this branch with `app_read` resource=Messages to learn what the user wants done, when, how
   success looks and what should happen on failure.
2. Collect what is already known: the task, a one-time date and time or a recurrence, the time
   zone (only if named; otherwise the host's), success criteria, retry (attempts, delay, condition),
   what to do with run branches by outcome, and whether the chat itself should be deleted.
   `Get` returns `localNow` and `hostTimeZone`: resolve "tomorrow", "every Monday" and similar
   against them exactly.
3. Ask the missing ones in one `ask_user` call of up to five questions (a second call only for
   what does not fit), in the user's language:
   - Task, only when the conversation does not say clearly what a run should do: free text.
   - When, if missing: for a repeating schedule `pickerKind: "recurrence"`; for one time
     `pickerKind: "date"` and/or `pickerKind: "time"` for the missing part only. Offer up to four
     presets as options with an exact `value` (recurrence JSON, `yyyy-MM-dd` or `HH:mm`) built from
     the request, the recommended one first. If it is unclear whether it repeats, ask with the
     recurrence picker and include a "Once" preset.
   - Success criteria, if missing: options drafted from the task plus free text.
   - Retry, if missing: options such as "Do not retry", "Up to 3 retries, 15 minutes apart" with a
     JSON `value` like `{"maxAttempts":3,"delayMinutes":15}`, plus free text for the condition.
   - Run branches, if missing: "Delete successful runs, keep failed and blocked (Recommended)",
     "Keep every run", "Delete every run after a day", free text for other delays.
   - The chat itself, if missing: "Keep the chat (Recommended)", "Delete it after the last run",
     a date to delete it on.
   A dismissed, expired, declined or interrupted question schedules nothing: say which value is
   still missing and stop. A question left empty in a partial answer is still missing; ask it once
   more on its own, and stop if it stays unanswered. Never choose a date, time or rule yourself.
4. Write the task as one self-contained instruction a run can follow without asking: what to do,
   with which inputs, and what to deliver. Show the final schedule in one `ask_user` question —
   the task, the recurrence in words, the first run, success criteria, retry and cleanup — with
   "Schedule it (Recommended)" and "Change something". A change request loops back to step 3 for
   that value only. Skip this question when every value, the task text included, came verbatim
   from the user.
5. If `newChat` is true or the user asked for a separate chat, create it with `app_chats`
   operation `Create` and a short title, and use its id below; otherwise use this chat.
6. Call `app_schedule` operation `Set` with `settings`: `task`, `recurrence` (picker values pass
   through unchanged), `timeZone` (empty for the host's), `successCriteria`, `retry`, `retention`
   with all three rules, and `deletion` only when the user wants the chat deleted. On an error,
   fix the reported value or ask about it, then call again.
7. For a new chat, open it with `app_navigate`.
8. Answer in one line: the schedule in words and the first run, without ids or revisions.
