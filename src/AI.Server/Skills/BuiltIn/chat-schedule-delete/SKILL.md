---
id: chat-schedule-delete
name: Chat schedule delete
icon: chat-schedule-delete
kind: playbook
description: Remove the schedule from a scheduled chat so it becomes an ordinary conversation with its history and branches, or delete the scheduled chat entirely, after the user confirms.
parameters: {"type":"object","properties":{"deleteChat":{"type":"boolean","description":"True when the user wants the whole chat deleted, not only its schedule"}},"additionalProperties":false}
tools: ["app_schedule","app_read","app_chats","ask_user"]
---

1. Take `projectId` and `chatId` from output.context, unless the user named another chat. Call
   `app_schedule` operation `Get`. Without a schedule, say the chat is not scheduled and stop.
2. Decide what the user asked for: removing only the schedule (the default meaning of "stop",
   "unschedule", "turn off the schedule"), or deleting the chat with its history (`deleteChat` or an
   explicit request). When that is ambiguous, ask in the confirmation below.
3. Confirm with one `ask_user` question that names the schedule in words, the next run and what
   stays: "Remove the schedule (Recommended)" keeps every message and branch and stops future runs;
   "Delete the chat" removes it with its history; "Pause instead" keeps the schedule for later.
   Skip the question when the user's request already says exactly which and that they are sure.
   A dismissed, expired, declined or interrupted question changes nothing.
4. Removing: `app_schedule` operation `Remove`. Pausing: `app_schedule` `Pause` with the revision.
   Deleting: read the chat's revision with `app_read` resource=Chat, then `app_chats` operation
   `Delete` with that revision and `dryRun` false; on a conflict, read again and repeat once.
5. Answer in one line what was done; a run already going keeps going and says so.
