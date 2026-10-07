---
id: team-assemble
name: Team assemble
icon: users
kind: playbook
aliases: ["team"]
description: Large task spanning several independent areas (API, UI, tests, modules): decide if a team pays off; if so, start a branch per teammate.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"The task"}},"additionalProperties":false}
tools: ["app_read","app_runs","app_navigate","ask_user","list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files"]
---

The user's instructions take precedence over this playbook. Ask questions and report results in
the user's language; write the charter and the briefs in that language too. This branch becomes
the team's coordination channel and this run its lead. How team messages work is described in
docs/34-asides-and-team-messages.md: every message `app_runs` submits carries this branch as its
sender, `intent` says what it is, and mode Aside adds information without starting a turn.

1. Take `projectId`, `chatId` and `branchId` from output.context. Work only from the chat's main
   branch (`branchId` equal to `chatId`); on another branch, say that a team is assembled from the
   main branch and stop. Read the branch with `app_read` resource=Messages for what is already
   known, and the project's files only as far as the analysis needs them.
2. Analyse the task before deciding anything:
   - **Goal and done**: what result is wanted, and how its completion is checked.
   - **Domain**: the terms of the task as a short glossary, so every teammate names things the same.
   - **Areas**: the parts of the system or of the subject the task touches.
   - **Plan**: phases in order, each with its outcome.
3. Decompose the plan into parts a single teammate could own: for each, its scope, the paths it
   owns (none may be owned twice), its inputs and outputs, what it depends on and its size.
   Contracts between parts (interfaces, data shapes, file formats) are fixed here, not discovered
   later. When one part implements a contract and another tests it, write the contract's edge
   cases as examples, input → output (empty values, separators, limits, paths that leave a root),
   so both read the same answer instead of each inferring its own.
4. Decide whether a team pays off. All of these must hold:
   - at least two parts can run at the same time;
   - their owned paths do not overlap, and shared files have one owner;
   - the contracts between them can be fixed now;
   - each part is substantial work, not a lookup.
   If one fails, do not assemble a team. Say which condition failed and recommend working in this
   branch, or `spawn_subtask` for short independent lookups whose results only need collecting.
   If the user asked for a team regardless, put both options to them with `ask_user` first.
5. Choose 2 to 4 teammates, or the size and roles the user asked for. Typical
   roles: architect, backend, frontend, tests and QA, reviewer, researcher, documentation, DevOps.
   Give each teammate one role, one scope, owned paths, the deliverable, its definition of done
   and the skills that fit its work (for example code-feature-implement, code-tests-add,
   code-review, qa-cases-create).
   Give each teammate an identity: a name taken in order from Ada, Bo, Cleo, Dan, Eva, Finn, Gia,
   Hal (skipping any this chat already uses) and its role in one or two words in the charter's
   language, written the same way everywhere — "Ada · Backend". The identity is fixed for the
   life of the team; the teammate's focus belongs to its scope, not to its name.
   Never make up an `operationId`: omit it, and the application assigns one and returns it as the
   result's `messageId`.
6. Show the proposed team with `ask_user`, labelled "Team": the plan in one line per phase and one
   line per teammate, with the options "Start the team (Recommended)", "Adjust the team" and
   "Work without a team"; `allowOther` on. Adjust and repeat on an answer; stop on "Work without a
   team", dismissed, expired or interrupted.
7. Write the charter into this branch: `app_runs` with `operation` `Submit`, `branchId` = `chatId`, mode `Aside`,
   intent `Decision`, no `operationId`, and content headed "Team charter" with: goal and done,
   glossary, phases, a table of teammates whose first column is the identity ("Ada · Backend"),
   then scope, owned paths, deliverable and done; the lead as "Lead"; the contracts; and the
   protocol below. Keep the result's `messageId`: it is the charter's id. Name teammates by their identity in the phases and contracts too. Make that call alone in
   its step: the aside joins this turn after the call's result, and the branches must start from it.
8. In the next step, read the main branch with `app_read` and confirm the charter message is there.
   Then for each teammate: `app_runs` with `operation` `Submit`, `branchId` = `chatId`, mode `Fork`,
   `parentMessageId` = the charter's id, no `operationId`, intent `Decision`, `wait` false, `memberName` and `role` = its identity (no `title`: the
   application names the branch "Name · Role" and signs its messages so), content = the teammate's
   brief: "You are <Name> · <Role> in this team. Run the team-contribute skill first." followed by the
   scope, owned paths, deliverable, definition of done, skills to use and whom to ask. Every branch
   inherits the analysis and the charter from the message it starts from, so the brief repeats
   only what is the teammate's own. Each result's `branchId` is that teammate's branch; every
   message it sends you carries the same branchId in its header.
9. Answer with the team as a table (identity, deliverable) and one line on how it works: the
   teammates report to this branch, which coordinates them with the team-coordinate skill. Do not
   wait for the teammates in this turn, and do not read their branches over and over: what they
   send arrives here as messages.

Protocol to put in the charter:
- Teammates write to the main branch with `app_runs` with `operation` `Submit`, `branchId` = the chat id.
- `status` goes as an Aside: it costs the lead nothing until its next step. `question`, `blocker`
  and `done` go as ordinary messages and wake the lead. No acknowledgements, no thanks.
- Every message is self-contained: what, why, and what is needed back.
- Everyone is named by identity — "Ada · Backend", "Lead" — in plans, decisions and reports; the
  application signs each message with its sender's identity, so nobody introduces themselves.
- A teammate changes only its owned paths; anything else is a question to the lead.
- A teammate starts changing its owned paths early and grows the result; reading without changing
  anything is not progress.
- A conflict between a contract, a reference and another teammate's work is a question to the lead
  as soon as it is found; until the lead decides, the charter's contract holds.
- Before each major step a teammate reads the main branch since the charter for new decisions.
- Teammates do not message each other; the lead routes what one needs from another.
- Nobody polls: waiting for an answer means ending the turn, and the answer arrives as a message.
  Reading the same branch again and again with nothing new in it stops the turn.
