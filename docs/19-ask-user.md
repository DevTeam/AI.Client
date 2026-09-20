# Asking the user (`ask_user`)

## Purpose

The `ask_user` tool gives the model a single way to stop and ask a human instead of guessing. It is needed where the request is genuinely ambiguous and a wrong guess costs a lot of work, or where the decision has a visible trade-off that belongs to the user: target version, refactor scope, naming, where new code goes. If the task is concrete enough to be done by stating an assumption in the answer, the model must do that instead, and this is written into the tool description.

## Mechanics

The tool lives in the in-process `App tools` server and is not different from the others by protocol: a regular schema, a regular `tools/call`. The only unusual part is that the call blocks until a person answers.

The wait is organized the same way as a tool confirmation (`PendingApproval`), because for the user it is one and the same fact — work has stopped because of them:

```
model → ask_user → IUserPromptBroker (= ChatRunDispatcher)
                      ├─ runtime.PendingPrompt + TaskCompletionSource
                      ├─ run snapshot → SSE → card in the feed
                      └─ answer ← POST /prompts/answer ← card / composer / CLI
```

`UserPrompt` is run-time state, not a storage record: a page reload preserves the question (it is in the snapshot), a host restart interrupts the turn and the question disappears with it. The answer becomes a normal tool-call result and lands in history exactly that way — a separate user message is not created.

### Run context

`IToolSessionFactory.OpenAsync` accepts `ToolRunContext(ProjectId, ChatId, BranchId, Interactive)`. The session is opened for one turn, so the tool learns its chat from the construction, not from an argument named by the model: otherwise the model could interrupt a conversation it has nothing to do with.

`IAppTool.Create(ToolRunContext)` is called for every session. `AppAskUserTool` is registered as a singleton, so the run is not held in it, but in the nested `Session` created on every `Create` call: otherwise two chats asking at the same time would answer for each other.

### Timeouts

The three limiters had to be separated explicitly:

| Limiter | What is done |
|---|---|
| `Patience` — tool silence (20–60 s from policy) | not applied to `ask_user`: a tool waiting on a human has not "gone silent" |
| `MaxCallDuration` = 30 min | not hit: the question's own timeout is 15 minutes |
| Turn deadline = 60 min | the time waiting for the human is returned to the budget (`TurnDeadline`). **Fixed for confirmations too**: a turn standing on a card has not "gone wrong", and the longer the human thought, the more likely it was to be killed |

The 15-minute expiry is not an error: `outcome: expired`, the turn continues without an answer. A person who stepped away has not refused.

### Policy

`ask_user` does not ask for confirmation: the `Ask` policy is skipped for it. The confirmation and the question itself put the same decision in front of the same person twice, and the first one conveys nothing that the second one does not. `Deny` keeps working — whoever does not want to be asked disables the tool like any other. The call limit is the shared `MaxCalls` from settings, with no special rule.

### Subtasks

In `spawn_subtask` nobody is watching the run, and it has no card of its own. The tool stays in the list but answers itself with `dismissed` and a direct message: "you are in a background subtask, there is no one to answer; do what you can, and name the unresolved choice in your final answer — the calling chat will ask it." This is the same line as confirmations, which answer `Deny` inside a subtask: the subtask is **told** "you cannot", so that it reports upward, instead of having its toolset silently reduced.

## Schema

```json
{
  "questions": [
    {
      "id": "scope",
      "label": "Scope",
      "text": "What should the refactor cover?",
      "multiSelect": false,
      "allowOther": true,
      "options": [
        { "label": "Only the changed module (Recommended)", "description": "…" },
        { "label": "All callers across the configuration" }
      ]
    }
  ]
}
```

| Constraint | Value |
|---|---|
| questions per call | 1–5, `id` is unique and non-empty |
| options per question | 0–8; zero is allowed only when `allowOther: true` |
| lengths | `text` ≤ 500, `label` ≤ 24, option ≤ 80, option description ≤ 160 |
| `allowOther` | defaults to `true` |

A violation is a tool error with text the model can correct itself by, not a schema rejection it can only repeat. Such a call does not reach the broker.

Result:

```json
{
  "answers": [{ "id": "scope", "selected": ["Only the changed module (Recommended)"], "other": null }],
  "outcome": "answered",
  "guidance": "Proceed on the answers given. These were left to you: tests. …"
}
```

The wire carries **positions** of the options from the UI; the model receives **texts**: an answer read from history without the question in front of you should still say what was decided. An answer naming an option that does not exist is discarded rather than guessed: the two sides of the transformation are separated by the process, and a mismatch means the question was replaced.

`outcome` is `answered`, `dismissed`, `expired`, `interrupted`, or `invalid`. In every case except the first, `guidance` tells the model to decide on its own, name the assumption, and **not ask the same thing again**.

## UX

The card sits at the end of the feed, where the confirmation card sits, and for the same reason: it is what the person should do next. Not a modal — the question belongs to the chat, and a neighboring chat has its own work going on.

| Decision | Behavior |
|---|---|
| Multiple questions | all at once in one card, one `Answer` button: answers are often interdependent |
| Partial answer | allowed. `Answer` is always active; questions with no selection are sent empty, and `guidance` names them one by one, so the model does not ask again |
| Refusal | `Decide yourself` button → `dismissed`. An empty "answer" also becomes `dismissed`: it is the same thing with a worse record |
| Composer | while the question is open, sent text becomes a free answer to the first question that accepts it. Placeholder is "Answer the question above…". A queue would mean a quiet deadlock: the person writes an answer at the bottom and gets it delivered after 15 minutes |
| Keyboard | standard radio/checkbox semantics — Tab, arrows, Enter. No custom hooks |
| Markdown | inline subset in the question text (emphasis, code, links). Options and descriptions are always plain text: they are button captions |
| "Other" | a field next to the option; typing text selects it. With zero options, the card looks like a regular input with the question as its heading |
| Attention | an "attention required" marker in the chat list, the same one a confirmation uses. No system notifications or sound |
| Trace | a tool-call row through `AskUserPresentationAdapter`: "Scope: Only the changed module", expands to details. An answer with no selection is `Warning`: the model decided on its own, and that is worth finding later |
| Language | English, like the whole UI. The model writes the question text in the conversation's language |

`RunStateService` does not count the time spent waiting for a question as generation time, so the "model is thinking" indicator does not lie while the person is reading.

## CLI

There is no separate "non-interactive" mode of the agent: the CLI is a thin client of the same Host and the same run, so the tool is not hidden and the human answers with a command:

```
session send …                → { "status": "awaiting_answer", "prompt": { … } }
session answer --session <id> --prompt <id> --question <id> [--select 0,2] [--text <…>]
session answer --session <id> --prompt <id> --dismiss true
```

Symmetric to `awaiting_approval` / `session approve`.

## Change composition

| File | Contents |
|---|---|
| `Contracts/Runs/UserPrompt.cs` | `UserPrompt`, `UserPromptQuestion`, `UserPromptOption`, `UserPromptAnswer`, `UserPromptResponse`, `UserPromptOutcome` |
| `Contracts/Runs/ChatRunSnapshot.cs` | `PendingPrompt` |
| `Application/Runs/IUserPromptBroker.cs` | `AskAsync`, `UserPromptRequest` |
| `Application/Runs/ChatRunDispatcher.cs` | broker implementation, `AnswerPromptAsync`, releasing the question together with the turn |
| `Application/Tools/IToolSession.cs` | `ToolRunContext` and its pass-through in `OpenAsync` |
| `Application/Tools/ChatAgent.cs` | `TurnDeadline`, bypassing `Patience` and confirmation for `ask_user`, `interactive` |
| `Infrastructure/Tools/*` | context pass-through through three factories |
| `Mcp.App/AppAskUserTool.cs` | tool, validation, position-to-text conversion |
| `Contracts/Tools/AskUserPresentationAdapter.cs` | transcript row |
| `Host/Program.cs` | `POST /api/projects/{p}/chats/{c}/prompts/answer?branchId=…` |
| `Web/Components/UserPromptCard.razor`, `MessageFeed.razor`, `Pages/Home.razor`, `Runs/*`, `Markdown/*`, `css` | card, composer, attention marker, inline markdown |
| `Cli/*` | `awaiting_answer`, `session answer` |

Tests: `ChatExecutionTests` — end-to-end scenario (a question stops the turn, the answer gets through, a stale answer is rejected, stop releases the question, the background run receives a denial immediately); `AppToolTests` — behavior of the tool itself (texts instead of positions, discarding a non-existent option, free text, validation, subtask).
