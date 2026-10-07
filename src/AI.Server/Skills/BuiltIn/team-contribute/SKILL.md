---
id: team-contribute
name: Team contribute
icon: users
kind: playbook
description: Work as a teammate in a branch: do the assigned part and report to the main branch.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read","app_runs","ask_user","list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run"]
---

The user's instructions take precedence over this playbook. Report in the language of the charter.
This branch is one teammate of a team assembled by team-assemble. The "Team charter" message and
the analysis before it are in this branch's history; the brief that started this branch is the
part that is yours. Team messages are described in docs/34-asides-and-team-messages.md.

1. Take `projectId`, `chatId` and `branchId` from output.context. The lead is the chat's main
   branch, whose id equals `chatId`. From the charter and your brief, restate for yourself: your
   role, scope, owned paths, deliverable, definition of done and the contracts you depend on.
2. Before starting and before each major step, read the main branch with `app_read`
   resource=Messages, `branchId` = `chatId`, for decisions made since the charter. A decision
   overrides your brief. A message the lead sends into this branch arrives as a new turn or, while
   you work, before your next step.
3. Do the work with the skills your brief names, the project's conventions and its checks. Change
   only your owned paths. A change anywhere else, or to a contract, is a question to the lead.
4. Write to the lead with `app_runs` Submit, `branchId` = `chatId`, a fresh `operationId`,
   `wait` false:
   - progress worth knowing: mode Aside, intent `Status`, at most once per phase;
   - something only the lead or the user can settle: mode Send, intent `Question`; then carry on
     with what does not depend on the answer, or end the turn saying what you wait for;
   - stuck with no way forward: mode Send, intent `Blocker`, with what you tried;
   - a decision others must follow that your scope lets you make: mode Aside, intent `Decision`.
   Every message is self-contained — what, why, and what you need back. No acknowledgements, no
   thanks, and no messages to other teammates: the lead routes them.
5. When the deliverable meets its definition of done, check it once more against the charter's
   contracts, then send intent `Done` (mode Send) with what changed (paths), how it was verified,
   and anything left open.
6. Answer in this branch with the same summary, or with what you are waiting for and why.
