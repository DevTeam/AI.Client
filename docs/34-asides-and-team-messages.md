# Asides and team messages

Status: `Accepted`. Scope: adding information to a branch without starting a turn, both for a person
at the composer and for a model in another branch of the same chat.

## Why

Before this change a message could only start a turn (Send), wait for one (Queue) or cut one short
(SendNow, Replace). Two things had no way through:

- **A person adding information to work in progress.** "By the way, the tests are in another
  folder" either waited behind the whole turn or interrupted it. Most of the time the model only
  needed to read it before its next step.
- **A model in one branch talking to another branch.** A team of models works in branches of one
  chat and coordinates through the main branch (see [Team work](#team-work)). Every message a
  teammate posted started a full turn of the lead and was stored as if the person had written it.

Both are the same act: *hand the branch some information; do not start a new turn for it.*

## Asides

An aside is a message submitted with `ChatSubmitMode.Aside`. What happens depends on the branch:

| Branch state | What happens |
|---|---|
| No command in flight (idle, completed, paused with only waiting messages) | The message is appended to the branch head at once, with delivery `Aside`. No model is called. |
| A command is in flight (generating, or interrupted/failed with its user message committed) | The message waits in the branch queue, marked as an aside. The running turn takes it at its next step boundary and the model reads it before its next request. It is stored with delivery `InTurn`. |
| The turn ends before another step boundary | Asides still waiting are appended right after the reply, with delivery `Aside`, before the next queued command starts. The model did not read them in that turn; they are part of the history from then on. |

### The step boundary

An aside enters a running turn only **after all results of a tool batch** and before the next model
request. Anywhere else it would split a tool call from its result, which every provider rejects. A
turn that answers without calling tools has no such boundary, so an aside sent during the final
answer becomes a plain aside after it.

Each aside joins the turn as a user message at the tool head, so the history stays a valid chain:
`assistant (tool calls) → tool results → user (aside) → assistant …`. The model sees it with a short
header saying it was added while the turn was running, so it is taken into account rather than read
as a new request that replaces the task.

### Retrying a turn

A retried turn rewinds the branch to its user message and prunes the abandoned attempt. Asides the
abandoned attempt had taken are put back into the queue, so the new attempt takes them again at its
first step boundary. Nothing the person added is lost by a retry.

### Turn boundaries

Every user message bounds a turn except an `InTurn` one, which belongs to the turn it joined: the
compact turn view keeps it among the steps, and the resume logic walks past it when looking for the
turn's user message. An `Aside` message stands between turns as an item of its own, without an
answer. Title generation counts only `Turn` messages.

The model-facing context is not regrouped: there an in-turn message is an ordinary user message
after complete tool results, which is a valid boundary for history checkpoints and compaction.

## Composer

| Keys | Idle branch | Running branch |
|---|---|---|
| Enter | Send | Queue at the tail |
| Ctrl+Enter | Queue | Queue |
| **Alt+Enter** | **Add as an aside** | **Add to the current turn** |
| Ctrl+Alt+Enter | Branch | Branch |
| Ctrl+Shift+Enter | Send | Interrupt and send now |

Alt+Enter is one action with one meaning: *give the model this, do not start a turn for it*. The
send button tooltip lists it like the other modes and names it by the branch state. The queue panel
shows a waiting aside with an "Aside" label: it is delivered at the next step, not when its turn
comes.

In the transcript an `InTurn` message is shown among the turn's steps with an "Added during the
turn" label, and an `Aside` message with an "Aside" label and no answer under it.

## Team messages

A message a model submits through `app_runs` carries its **sender**: the chat and branch of the run
that submitted it, filled in by the server from the tool run context. A model cannot set or forge it,
and the HTTP API never accepts one from a client. An optional **intent** says what the message is:

| Intent | Meaning |
|---|---|
| `question` | Needs an answer before the sender can continue |
| `answer` | Answers a question |
| `decision` | Fixes something the others must follow |
| `status` | Progress, nothing to act on |
| `blocker` | The sender is stuck and cannot continue on its own |
| `done` | The sender's part is finished; the message is its result |

`app_runs` Submit accepts `mode: Aside` and `intent`. A teammate reports `status` as an aside, which
costs the lead nothing until its next step or turn, and sends `question`, `blocker` and `done` as
ordinary messages that wake the lead.

A message another branch's run sends as an ordinary message (`question`, `answer`, `blocker`,
`done`) **joins a turn in flight**: while the receiving branch has a command in flight, the turn
takes it at its next step boundary exactly like an aside, so a busy lead reads a report before its
next step instead of polling for it and then spending a whole turn on it afterwards. If that turn
ends before another boundary, the message stays queued and runs as a turn of its own, because it
needs an answer. The queue row names the sending branch and the intent. A person's messages keep
their order in the queue.

The model reads a message with a sender under a header naming the sending branch by its current
title and the intent, for example `[From branch "Backend — orders API" · question]`. The transcript
shows such a message as a card of that branch instead of a bubble of the person: the branch title,
the intent and a link that opens the branch.

## Team work

A team is a chat whose main branch is the coordination channel and whose other branches are the
teammates. Nothing beyond this document's mechanics is needed in code; the protocol lives in skills:

- `team-assemble` (main branch): analyses the task (goal and done, domain glossary, areas, phased
  plan), decomposes it into parts with owned paths and contracts (a contract one part implements and
  another tests carries its edge cases as input → output examples), and decides whether a team pays
  off — at least two parts that run at once, disjoint owned paths, contracts fixable up front, each
  part substantial. If not, it says so and recommends one branch or `spawn_subtask`. Otherwise it
  proposes 2–4 teammates, asks the user to confirm, writes the "Team charter" into the main branch
  as an aside and forks one branch per teammate from it, named "Role — focus" by the fork's `title`.
- `team-coordinate` (main branch, the lead): handles team messages by intent — answers questions,
  unblocks, records decisions and sends them to the affected branches as asides, checks `done`
  against the definition of done, moves the team between phases and integrates the result.
- `team-contribute` (teammate branch): works only in the owned paths, reads the main branch for
  decisions before each major step, and reports by the protocol: `status` as an aside, `question`,
  `blocker` and `done` as messages that wake the lead. It pins the file-writing tools and
  `run_skill`, starts the skills its brief names, and makes a first change in its owned paths early
  instead of reading on: a teammate that started with read-only tools once spent three hours and
  some 150 reads without writing a file. A conflict between the charter's contract, a reference
  and another teammate's work goes to the lead as a `question` at once, and the teammate carries on
  by the charter's contract until the lead decides.

Every teammate branch starts from the charter, so it inherits the whole analysis without copying it.
The charter is written as an aside in its own step: it joins the running turn after that call's
result, which is what makes it a message the branches can start from. The forks themselves are
submitted from the main branch's run, so each brief shows the main branch as its sender.

`team` is a skill domain for these playbooks.

### Being offered

Users rarely ask for a team by name, so `team-assemble` is reached in four ways:

- **The router.** `skill-route` puts it first for a large request spanning several independent areas
  that could proceed at the same time (an API with its UI and tests, several modules or services,
  separately researched options) or one that mentions a team, roles or parallel work — never for a
  change in one area, a bug, a question or a continuation. A doubtful choice costs one analysis:
  the playbook decides whether a team pays off and falls back to one branch.
- **Its description** names those signals, for the catalog the model reads.
- **`/team`** invokes it directly.
- **Planning skills** (`code-plan`, `code-feature-implement`, `qa-plan`) offer it in one sentence
  when their plan splits into parallel parts on disjoint files; they never start a team themselves.

`evals/AI.Routing.Evals` measures the router on messages that should and should not reach it (see
[Context request budgets and quality evaluation](31-context-evaluation.md#opt-in-model-quality-evaluation)).

The protocol lives in these three playbooks only; other skills stay unaware of teams. Where a
general skill would conflict, the team playbook overrides it: a teammate never changes the git
state of the shared working directory (committing is the lead's, at integration) and takes
questions to the lead before the user. `code-plan` mentions `team-assemble` when its plan splits
into parallel parts on disjoint files, and the help of the send button and the branch row tells
the guide about asides and team branches.

A loaded playbook stays active only for the last few user messages (see
[Skills](27-skills.md)), and team messages are user messages. So a message that carries an intent
and comes from the same chat names the protocol in its header: "team message from the lead: keep
to the team-contribute protocol" in a teammate's branch, "team message to the lead: handle it with
the team-coordinate protocol" in the main branch. A fork or a message without an intent gets no
such line.

Nobody polls. Waiting for an answer means ending the turn: the answer arrives as a message and wakes
the branch. Reading the same branch again and again brings nothing new and stops the turn as a
stall. A teammate whose turn does stop that way reports it by itself: the dispatcher sends the
model's account of the stall to the main branch as a `blocker` from that branch, which wakes the
lead (see the stall rule in [Hidden model instructions](22-hidden-model-instructions-and-run-completion.md)).
A teammate's branch is recognised by its first message: a team message the main branch sent.

### Identity

Every teammate has one identity, `Name · Role` ("Ada · Backend"), stored on its branch as
`ChatBranch.Member` and shown the same way everywhere:

- `app_runs` Fork takes `memberName` and `role` instead of `title`; the branch is named
  "Name · Role" by the server. The name is unique in the chat (case-insensitive) and is refused at
  submission, while the lead can still pick another. Names come in order from a fixed list in
  `team-assemble` (Ada, Bo, Cleo, …), so the model does not invent them.
- The chat gives each teammate the first accent swatch no other teammate wears (teal, amber,
  purple, green, pink, orange, indigo, then blue). The colour is drawn as a dot in the sidebar's
  branch row and in the queue, and as the left edge and dot of the teammate's cards in the
  transcript. Only a known swatch reaches a style, as the component's `--member-color`.
- Message headers, sender cards and queue rows name a teammate by its identity, not by the branch
  title, so renaming the branch by hand changes nothing about how it is signed. In a chat with
  teammates the main branch is "Lead" (the model reads "From the lead").
- The charter's teammate table starts with the identity and the branchId; the skills tell everyone
  to name each other only by identity in plans, decisions and reports.

### Failures

A team runs with nobody watching each branch, so a failure must neither stop the team silently
nor wait for someone to press Retry:

- **A broken stream is retried by the agent.** When a model step's stream breaks off after it has
  started — falls silent past the idle timeout, ends in the middle of a tool call, or loses its
  connection (`ChatStreamInterruptedException`, `IOException`) — the agent asks for the step again,
  up to three times in a row, after 2, 4 and 6 seconds. Nothing of the broken step was persisted
  or executed, so this is the same as Retry. This applies to every chat, not only teams.
- **A teammate's failed turn is reported to the lead** as a `blocker` beginning with "Failed:";
  `team-coordinate` resumes that branch once with `app_runs` Resume and tells the user if it fails
  again. A turn stopped for lack of progress is reported the same way.

### Known limits

- Branches share the project's working directory. The charter assigns owned paths to each role;
  nothing in code enforces them.
- The connection is chosen per chat, not per branch.
- A tool with an `Ask` policy stops a teammate branch until the person answers; the sidebar marks the
  branch as needing attention.

## Storage

`ChatMessage` gains two optional fields, written only when set:

- `Delivery`: `Turn` (default, omitted), `Aside` or `InTurn`.
- `Sender`: `{ ChatId, BranchId, Intent }`.

A queued run message gains `IsAside` and `Sender`. Documents written before this change read as
ordinary turn messages without a sender.
