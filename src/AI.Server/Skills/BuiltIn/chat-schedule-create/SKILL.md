---
id: chat-schedule-create
name: Chat schedule create
icon: chat-schedule-create
aliases: ["schedule"]
kind: playbook
description: Set up a task to run later or repeatedly — once at a date and time, every day, every weekday, every Monday, hourly or monthly — by turning this chat, or a new chat, into a scheduled chat instead of doing the task now; asks the user for every missing time, success, retry and cleanup rule, allows the tools and directories its runs need so a run never waits for a person, changes the chat's kind and starts automatic runs.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"What each run should do, if the user said it"},"when":{"type":"string","description":"When it should run, in the user's words"},"newChat":{"type":"boolean","description":"True when the user wants a separate new chat instead of this one"}},"additionalProperties":false}
tools: ["app_read","app_schedule","app_chats","app_security","ask_user","app_navigate"]
---

Do not carry out the task in this turn: the user asked for it to run on a schedule, and this skill
only sets the schedule up. The first run does the work.

A scheduled chat keeps its history. At every occurrence the application forks it from the end of
its main branch, and that run branch carries out the task with the `chat-schedule-run` skill. Nobody
is watching a run: a tool call whose effective policy is `Ask`, or a path outside the project's
directory grants, does not fail — it leaves the run `Blocked`, waiting for a person who may not be
there. So setting a schedule also means making the run able to finish on its own, and the schedule
the user gets must never depend on someone being at the keyboard.

Never invent a value the user did not give and never fill one in silently: every missing value is
asked with `ask_user`. Ask only for what is missing; a value the user already named in this chat, in
the branch or in the arguments is never asked again.

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
4. Write the task as one self-contained instruction a run can follow without asking anyone: what to
   do, with which inputs — the files, directories and services it touches — and what to deliver.
   Those inputs, not a guess, are what the next two steps prepare.
5. Work out what the run will call and touch, from the task text and this conversation: the tools
   it needs (file tools, `process_run`, a search or fetch server, a configured MCP server, and so
   on) and the directories it will read or change. Then read the current state with `app_read`:
   resource=Project for the directory grants and project tool policies, resource=Settings for the
   servers and their global policies, and resource=Chat for the chat's own policies (read the new
   chat once it exists, in step 10). For every server in play, read `app_read` resource=McpTools
   with its id to get each tool's exact identity `(serverId, name, schemaHash)`; never guess one and
   never use the model's own prefixed function name.
6. Work out each needed tool's effective decision: chat overrides project overrides global, a
   disabled or denied server denies all of its tools, and an unknown tool is `Ask`. Only `Allow`
   runs with nobody there; `Ask` leaves the run Blocked and `Deny` refuses it outright.
7. For every needed tool that is not already `Allow`, and that the user did not ask to keep asking,
   put the tools before the user in one `ask_user` question — what each does and why the run needs
   it — with "Allow them for this chat (Recommended)" and "Keep asking me (leave them at Ask)".
   Applied tools get chat-scope policies only, so nothing else in the project changes:
   `app_security` operation `SetChatToolPolicy` with `projectId`, the chat's id, a fresh
   `operationId` per distinct change and `toolPolicy` containing the identity, `Allow` and limits.
   Use the starting values the tool-configuration skills use: `maxCallsPerRun` 128 for a bounded
   read, 32 for a write or an unknown tool, 8 for something destructive or externally visible, and
   `timeoutSeconds` 120 — or 600 for `process_run`, `cs_run` and `trigger_wait`, so builds, tests
   and waits can finish. A tool that manages its own permissions, such as `app_security`, stays
   `Ask`: never grant it `Allow`. Never shadow an inherited `Deny` — a denied tool is reported to
   the user as unavailable for the run.
   When the user keeps a tool at `Ask`, say plainly that the run will stop and wait for them at that
   call, and go on only if they still want the schedule.
8. Check the directories the task needs against the project's recursive grants and each grant's
   `toolNames`: a path without a grant makes the call fail, and a grant without `write` or `edit`
   makes a change fail. For every missing path or missing capability, ask in `ask_user` with
   `pathKind: "directories"` for the paths and "Read only (Recommended)" or "Read and write" for
   the access, then add each with `app_security` operation `AddDirectoryGrant`, the project's
   current revision, a fresh grant id, `recursive` true, the final segment as the display name and
   `toolNames` `["read"]` or `["read","write","edit","delete"]`, using each result's revision for
   the next. Never grant a broad root such as a whole drive without saying so in the question.
   If the user declines, say which part of the task cannot run, and schedule it only if they still
   want to.
9. Show the final schedule in one `ask_user` question — the task, the recurrence in words, the
   first run, success criteria, retry and cleanup, and which tools and directories the run was
   allowed — with "Schedule it (Recommended)" and "Change something". A change request loops back to
   step 3 for that value only. Skip this question only when every value, the task text included,
   came verbatim from the user and no access had to be changed.
10. If `newChat` is true or the user asked for a separate chat, create it with `app_chats`
    operation `Create` and a short title, and use its id from here on; otherwise use this chat.
11. Apply the decisions from steps 7 and 8 to the chat that will run the task, checking `Applied`
    and `Error` after every call and skipping anything already in place. On a refused write, say
    what is missing and ask whether to schedule anyway — a run without that access waits for a
    person or fails. Do not change the chat's approval mode, and do not touch the current chat's
    policies when the task runs in the new chat.
12. Call `app_schedule` operation `Set` with `settings`: `task`, `recurrence` (picker values pass
    through unchanged), `timeZone` (empty for the host's), `successCriteria`, `retry`, `retention`
    with all three rules, and `deletion` only when the user wants the chat deleted. On an error,
    fix the reported value or ask about it, then call again.
13. For a new chat, open it with `app_navigate`.
14. Answer in one line: the schedule in words, the first run and the tools and directories allowed
    for it, without ids or revisions.
