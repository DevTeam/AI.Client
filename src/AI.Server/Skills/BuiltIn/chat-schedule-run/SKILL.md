---
id: chat-schedule-run
name: Chat schedule run
icon: chat-schedule-run
kind: playbook
description: Run the scheduled task of this chat — inside a scheduled run branch carry out the task, check its success criteria and report the outcome; anywhere else start a run now. Used by the application's dispatcher for every scheduled run.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_schedule","app_read","ask_user","tool_search"]
---

1. Take `projectId`, `chatId` and `branchId` from output.context. Call `app_schedule` operation
   `Get`. Without a schedule, say the chat is not scheduled and offer `chat-schedule-create`.
2. Find the run in `runs` whose `branchId` is this branch (or the branch this one was forked from)
   with status `Running` or `Blocked`. If there is none, the user asked for a run now: call
   `app_schedule` operation `RunNow`, answer in one line that the run starts within seconds in a new
   branch, and stop.
3. You are the run. The messages above this one are the conversation that set the task up; the
   first message of this branch states the run number, the task, the success criteria and the retry
   condition. Carry out the task with the tools it needs, discovering them with `tool_search`. No
   one is watching a scheduled run: ask with `ask_user` only when the task cannot go on without a
   person's decision — the run waits, and the chat shows that it needs attention.
4. Check the success criteria against what actually happened, with evidence: output, files, a
   response, a measured value. Without criteria, success means the task was done completely.
   Never report success for work that was skipped, failed or only planned.
5. Call `app_schedule` operation `ReportRun` with `succeeded`, a one-line `summary` of the result
   (at most 200 characters, in the user's language) and, on failure, `retry`: false when the retry
   condition says this failure is not worth retrying, true otherwise.
6. End with a short answer: the outcome and the result itself, the way the task asked for it.
