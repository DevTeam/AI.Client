---
id: chat-plan
name: Chat plan
icon: list-checks
kind: playbook
aliases: ["todo", "progress"]
description: Keep a visible progress checklist with weighted steps while doing a multi-step task in this turn; not for planning a change without doing it.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"The task to plan, only when it differs from the user's latest message"}},"additionalProperties":false}
tools: ["plan_update"]
---

The plan is what the user watches while you work: a card above the composer shows the current step
and an overall progress bar, and the chat keeps the final state with the answer. It is a progress
report, not a design document, and it does not replace the work: lay it out, then do the task in the
same turn. Write step titles in the user's language.

1. Plan only a task of three or more distinct steps. For a question, an explanation or a single
   small change, skip the plan and do the work.
2. Break the task into 3–8 steps in the order you will do them, each a short imperative phrase of
   at most ~7 words that names an outcome ("Add the parser tests", not "Tests"). Investigation,
   implementation, verification and reporting are separate steps when each takes real work. Do not
   add steps for things you will not do.
3. Weight each step 1–10 by its expected share of the effort relative to the others: reading a few
   files is 1–2, a focused change 3–5, a broad change or a long test run 6–10. The bar moves by
   weight, so an honest weight keeps it honest; do not give every step the same weight unless they
   really are alike.
4. Call `plan_update` once with all `steps`, the first one `InProgress`, and a short `title`
   for the task. Then start the work.
5. Move the plan as the work moves, putting several changes in one `updates` call:
   - mark a step `Completed` as soon as it is done and the next one `InProgress` in the same call;
   - during a long step, report `progress` 0–100 when it has clearly advanced (a quarter, a half),
     not after every tool call;
   - `Skipped` a step that turned out unnecessary and `Failed` one that cannot be done, each with a
     one-line `note` saying why;
   - when the work shows the plan was wrong, lay out a corrected plan with `steps`: steps that keep
     their titles keep their times.
6. Before the final answer, every step is `Completed`, `Skipped` or `Failed`. Do not repeat the
   plan in the answer; report the outcome, and name any failed or skipped step and what it leaves
   undone.
7. When the user's next message continues the same task, keep moving the same plan. When it is a
   different task, lay out a new plan for it, or mark the remaining steps `Skipped` if the new task
   needs none.
