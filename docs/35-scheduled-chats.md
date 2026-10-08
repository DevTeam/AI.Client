# Scheduled chats

Status: implemented.

A scheduled chat is an ordinary chat whose state holds a schedule: a one-time date or a recurrence,
what each run does, how a run tells success from failure, what happens after a failure, what
happens to run branches, and whether the chat itself is deleted later. The Host's dispatcher runs
it: at every occurrence it forks the chat from the end of its main branch, and the new branch
carries out the task with the `chat-schedule-run` skill and reports how it went.

Any conversation can become scheduled without losing its messages or branches, and can become an
ordinary conversation again. It is set up from the chat itself (the `chat-schedule-*` skills over
the `app_schedule` tool), from another chat (the same skills naming a chat), or in the Schedule
widget. In the chat list a scheduled chat has a stopwatch icon.

## Terms

| Term | Meaning |
| --- | --- |
| Scheduled chat | A chat of kind `scheduled`; its versioned state is a `ChatSchedule` |
| Schedule | `ChatScheduleSettings` (task, recurrence, time zone, success criteria, retry, run-branch retention, chat deletion) plus the dispatcher's runtime fields |
| Occurrence | One moment the recurrence names |
| Run | One fork of the chat made for an occurrence, a retry or a "Run now" |
| Dispatcher | `ChatScheduler` with `ScheduledChatPass`: the Host component that starts, watches and cleans up runs |

## State

The schedule is the chat's kind state (`Kind = "scheduled"`, `KindStateVersion = 1`) and is read and
written through the chat contract like other chat metadata (`ChatDetails.KindState`). The types live
in `AI.Contracts.Schedules` so the Host and the Web share them:

| Type | Holds |
| --- | --- |
| `ScheduleRecurrence` | `frequency` (Once, Hourly, Daily, Weekly, Monthly, Yearly), `start` (`yyyy-MM-dd`), `time` (`HH:mm`), `interval`, `weekdays`, `monthDay` (1–31, -1 last), `monthWeekday` (`{ordinal 1–4 or -1, day}`), `until`, `count` |
| `ChatScheduleSettings` | `task`, `recurrence`, `timeZone` (accepted as an IANA or Windows id, stored and read back as IANA: the browser does not know Windows ids), `successCriteria`, `retry`, `retention`, `deletion` |
| `ScheduleRetry` | `maxAttempts` (1–10 retries after the first attempt), `delayMinutes`, `condition` in the user's words |
| `ScheduleBranchRetention` | One `ScheduleRetentionRule` (`Keep` or `Delete` after `delayMinutes`, 0 = at the next pass) per outcome: `succeeded`, `failed`, `blocked` |
| `ScheduleChatDeletion` | `at` (absolute) or `afterLastRunMinutes` (once nothing is left to run). Absent: the chat stays |
| `ChatSchedule` | `settings`, `paused`, `revision`, `nextRunAt`, `retryAt`, `runRequestedAt`, `runNumber`, `runs` (the last 50 `ScheduleRunRecord`s) |
| `ScheduleRunRecord` | number, branch, occurrence, start, attempt, status (Running, Blocked, Succeeded, Failed, Skipped, Missed), manual, finish, the outcome the run reported, summary, branch deletion |

Wall-clock times are in the schedule's time zone. A time the clocks skip fires when they resume; an
hour they repeat fires the first time round. A month too short for `monthDay` fires on its last day.
`IScheduleCalendar` computes occurrences and validates settings; `IScheduleDescriptions` says them in
words ("Every weekday at 09:00") and names run branches. `ChatThread.SetKind` and
`IChatService.ChangeKindAsync` change a chat's kind and state under the chat's lease;
`IChatScheduleStore` turns every schedule write into a function of the latest stored schedule, so
the widget, the tools and the dispatcher never undo one another.

Only a `conversation` can become scheduled. A schedule whose recurrence has no occurrence after now
is refused with a message saying so. Setting a schedule on an empty chat writes the task as its first
user message: an empty chat is hidden from the list and has nothing to fork runs from.

## Dispatcher

`ScheduledChatKindPolicy` starts the dispatcher when the Host starts and stops it with the Host.
`ChatScheduler` is one loop. It lists the scheduled chats of every project, keeps each schedule by
the chat revision it was read at (chat documents carry their whole history), and processes a chat
when something is due: an occurrence, a retry, a "Run now", a branch or chat deletion, or a run that
is going (looked at every 5 seconds). It sleeps until the earliest due moment, at most 30 seconds,
and wakes at once when a schedule changes or a run reports. A failure is logged and never stops the
loop or the other chats. Every processed chat publishes the `data-changed` signal, so open windows
re-read it.

`ScheduledChatPass` does, in order:

1. **Watch the run that is going.** A pending approval or question, or an interrupted or paused run
   that still holds its command, makes it **Blocked**: the run waits for a person, with the usual
   attention marker in the chat list. When the `blocked` rule says Delete, the branch is deleted that
   long after the run blocked, which stops it. A completed run finishes with the outcome it reported;
   one that reported nothing has **Failed** ("ended without reporting an outcome"). A failed run fails.
2. **Finish a run.** Its record gets the outcome and summary; its branch is renamed with the outcome
   (`#12 · Tue 7 Oct 09:00 · succeeded`) and scheduled for deletion by the rule for that outcome. A
   failed scheduled run is retried after `retry.delayMinutes` while attempts remain, unless the run
   reported `retry: false` (the retry condition said it is not worth it). "Run now" runs are not retried.
3. **Delete run branches** whose rule is due, each with every branch forked from it, deepest first.
4. **Delete the chat** when `deletion.at` has passed, or `afterLastRunMinutes` after the last run once
   no occurrence or retry is left, and no run is going.
5. **Start the next run.** One run goes at a time: an occurrence that comes while one is going is
   recorded as **Skipped**. An occurrence found more than two minutes late (the application was
   closed) is recorded as **Missed** and not run; the next one is after now. While paused nothing
   starts, and resuming skips the occurrences that passed meanwhile.

A run is a fork of the main branch's head (`ChatSubmitMode.Fork`, `MessageParentMode.BranchHead`, or
`Root` for an empty chat), submitted as an interactive run, so approvals and `ask_user` wait for the
person as in any chat. Its message carries the `chat-schedule-run` skill chip and states the run
number, the moment, the task, the success criteria, the retry condition and the instruction to report
with `app_schedule ReportRun`. The run sees the conversation that set the task up and nothing a
previous run did. Its branch title says when it started and how it ended:

```
#12 · Tue 7 Oct 09:00                 started by the schedule
#12 · Tue 7 Oct 09:00 · retry 1/3     a retry of the same occurrence
#13 · Tue 7 Oct 14:32 · manual        started by "Run now"
#12 · Tue 7 Oct 09:00 · succeeded     after it finished
```

## Tools

`app_schedule` (App tools server) is the chat-editing tool for schedules; projectId and chatId default
to the current chat:

| Operation | Effect |
| --- | --- |
| `Get` | The schedule, its description in words, the next three runs, and the host's `localNow` and `hostTimeZone` so a model can resolve "tomorrow" exactly |
| `Set` | Creates or replaces the schedule from `settings`; turns a conversation into a scheduled chat |
| `Pause`, `Resume` | Stop or start runs; take the schedule `revision` |
| `Remove` | The chat becomes an ordinary conversation, keeping messages and branches |
| `RunNow` | Asks the dispatcher for a run now |
| `ReportRun` | From a run branch (or a branch forked from it): `succeeded`, a one-line `summary`, and on failure `retry` |

Writes take a fresh `operationId`, are replayed by it, and publish `data-changed`. A stale schedule
revision answers `Conflict` with the current schedule. The tool description tells the model never to
invent a date, time, recurrence or rule and to ask for missing ones with the schedule pickers of
`ask_user`. The same service (`IChatScheduleService`) answers
`GET/PUT/DELETE /api/projects/{p}/chats/{c}/schedule` and `POST …/schedule/pause|resume|run`, which
the widget uses.

## Skills

| id | What it does |
| --- | --- |
| `chat-schedule-create` | Turns this chat, or a new one, into a scheduled chat. It reads what the user already said and asks with `ask_user` only for what is missing — date, time or recurrence through the pickers, success criteria, retry, run-branch rules, chat deletion — never filling a value in silently; a dismissed or unanswered question schedules nothing |
| `chat-schedule-edit` | Changes, pauses or resumes a schedule, asking only for values not named, and confirms old and new values side by side unless the user gave them exactly |
| `chat-schedule-delete` | Removes the schedule (keeping the chat), pauses it instead, or deletes the chat, after one confirmation |
| `chat-schedule-run` | In a run branch: carries out the task, checks the success criteria with evidence and reports with `ReportRun`. Anywhere else: starts a run now. The dispatcher puts it on every run message |

Each has its own stopwatch icon in the shared skill icon set.

## Widget

The Schedule widget (`chat-schedule`, `stopwatch`) is available in every chat:

- **Ordinary chat** — says the chat runs only when written in and offers *Schedule this chat*, which
  opens the settings form prefilled with the first user message as the task, daily at 09:00
  tomorrow, this device's time zone and the default branch rules — every value visible and editable
  before *Schedule* saves it.
- **Scheduled chat** — leads with a countdown to the next run or retry (ticking every second in its
  last hour), a status pill (Active, Paused, Running #n, Waits for you, Finished), the next run and
  the recurrence in words, *Run now*, *Pause*/*Resume* and *Edit*; then the task, success criteria,
  retry, the three branch rules with outcome colours, chat deletion and time zone; then the recent
  runs, newest first, each with its outcome, attempt or "manual", summary and pending deletion, a
  click opening its branch while it exists; and *Remove the schedule* with an inline confirmation.
  A run waiting for a person shows a warning strip with *Open run*. The folded header says "in 2h 14m",
  "Paused" or "Not scheduled".

The settings form (`ScheduleSettingsForm`) holds the task, the recurrence editor, success criteria,
retry (attempts, delay, condition), a rule per outcome, chat deletion (keep, after the last run, or on
a date and time). It has no time zone field: a schedule set in the form uses this device's time
zone. Only a schedule in another zone (set by a tool, or on another device) says which, with *Use my
time zone* to switch to the local one; wall-clock times stay as they are. Refusals from the Host appear in the widget in its words, and while the form is invalid a note under it says why Save is unavailable.

## Run branches in the branch picker and the sidebar

A scheduled chat gains a branch per run, so both places that list branches group them. Runs are
recognised from the schedule (`runs[].branchId`), never from their titles.

- **Branch picker** (`BranchPickerMenu`): a few lines stay a plain list. With more than seven it gets a
  filter box that takes the keyboard at once (↑ ↓ move, Enter opens), splits *Conversation* from
  *Scheduled runs*, filters runs by outcome with counted chips, and shows the newest six runs plus
  *Show all*; the current line is always shown. A run reads `#12 · 8 Oct 09:00` with an outcome dot
  and word and a `manual` or `retry n` tag; its full title and summary are the tooltip. The menu never
  grows wider than the feed it opens in.
- **Sidebar**: the runs sit under one *Runs* row with a dot and count per outcome, which is always
  shown while the chat has runs. Up to six runs show unfolded; more fold until the row is clicked, and
  a click on the row folds or unfolds them from then on. Unfolded, it shows the newest six and
  *Show all N runs*; folded, only the current run stays visible. Today's runs show
  their time alone, older ones their date as well. Folding, unfolding and *Show all* keep the phone drawer open (`data-stays-in-drawer`);
  opening a run closes it as any navigation does.
- **Clearing them out**: the chat's menu offers *Delete all branches (N)* — every branch, the main
  conversation stays — and a branch's menu *Delete all sub-branches (N)* — every branch below it, at
  any depth, the branch itself stays. Both ask once, delete deepest first against the current chat
  revision, and move the view to the remaining branch when the open one was among them.

A run's branch has its own icon in both places: an outlined play that takes the run status exactly as
a branch icon does — bright while generating, coloured for an unread answer, a question or an
approval, a failure — with the run's outcome as a small dot in its corner (none while it runs). Runs
not opened yet are always listed, even while the sidebar's Runs row is folded or they are older than
the newest few, and the Runs row says how many there are ("2 new", coloured by the most urgent one).
Opening a run clears its status.

## From the message box

Typing a request and pressing Enter starts a turn, which is wrong for a task meant to run later.
**Alt+Shift+Enter** (or the send button while those keys are held) sends nothing: it opens the
Schedule widget — shown and unfolded even if it was hidden — with the composer text as the task and
the keyboard in the editor. In an open chat it schedules that chat (in a scheduled chat it edits the
schedule with the new task); with no chat open the widget offers *Schedule a new chat*, and saving
creates the chat with the task as its first message, which the model first sees in the first run.
The composer is cleared only after the schedule is saved.

`/schedule` (the alias of `chat-schedule-create`) does the same in words: the skill sets the schedule
up, asking for whatever is missing, and is told not to carry out the task in that turn. Its
description names the usual phrasings ("every day", "every Monday", "at 9:00") so the router picks
it for such requests even without the alias.

## `ask_user` schedule pickers

`pickerKind` `date`, `time` and `recurrence` open a calendar, a clock or the recurrence editor inside
the question card; see [Asking the user](19-ask-user.md#schedule-pickers). Every picked value comes
back in `values` in one spelling — `yyyy-MM-dd`, `HH:mm`, or recurrence JSON in the shape
`app_schedule` takes — with `valueDescriptions` for recurrences, so a schedule is stored without
guessing. Options may carry exact `value`s as presets; choosing one fills the picker, and adjusting the
picker then is the answer.

## Guide tour

The application guide has a *Scheduled chats* topic (`app-guide-schedules`). Its routes: what a
scheduled chat is; setting one up without sending (Alt+Shift+Enter, the Schedule widget, `/schedule`);
reading a schedule; following runs in the branch picker and the sidebar; pausing, changing and
removing a schedule — taught with show steps beside the real controls, never by changing a schedule.

When the open chat is not scheduled, the tour offers a demo: `app_navigate` with
target=`chat.demo_schedule` creates *Guide schedule demo* without a model (`IScheduleDemo`): a request
to post the euro rate every weekday at 09:00, the answer that scheduled it, and five runs kept as
branches — #2 failed, #3 is its retry — with their run messages, answers and summaries. Its schedule
is marked `Demo`: the dispatcher never starts or watches anything for it, and the widget says so. The
first Set, Pause, Resume or Run now makes it an ordinary schedule. The guide's clean-up removes the
demo only while it is still a demo with nothing written in it.

## Verification

- `ScheduleCalendarTests`: weekdays, every other week, last day and last weekday of a month, short
  months, `until`/`count`, hourly arithmetic, daylight saving, validation, picker value spelling and
  run titles.
- `ChatExecutionTests.Schedules`: through the shipped graph — a conversation becomes scheduled, a pass
  forks from the end, a reported success deletes the branch by rule, a run without a report fails,
  keeps its branch, is renamed and retried from the end, an occurrence during a run is skipped and a
  late one missed, pausing and removing keep the conversation, invalid schedules are refused, an
  empty chat gets its task as the first message, and a going run is watched again after a few seconds.
- `AppToolTests.Schedules`: `ask_user` date, preset and recurrence values over the in-process MCP
  transport, invalid presets, and `app_schedule` Set/Get/Pause/ReportRun/Remove with conflicts.
- `ChatScheduleWidgetRenderingTests` and `ChatSchedulePresentationTests`: the widget in every state,
  the pickers in a question card, countdown and wording.
