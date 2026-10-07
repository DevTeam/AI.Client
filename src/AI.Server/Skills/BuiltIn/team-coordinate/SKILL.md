---
id: team-coordinate
name: Team coordinate
icon: workflow
kind: playbook
description: Lead a team from the main branch: answer teammates, record decisions, unblock, track status, integrate.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read","app_runs","app_chats","app_navigate","ask_user","list_directory","search_files","grep_files","read_text_file","read_multiple_files","process_run"]
---

The user's instructions take precedence over this playbook, and the user's messages in this branch
come first. Ask questions and report results in the user's language. This is the main branch of a
team assembled by team-assemble; its "Team charter" message names the teammates, their branches,
owned paths, deliverables and the protocol (docs/34-asides-and-team-messages.md).

1. Take `projectId` and `chatId` from output.context. Find the charter in this branch with
   `app_read` resource=Messages and the teammates' branches with resource=Chat. A message from a
   teammate starts with a header naming its branch, its branchId and its intent; asides are
   information that asked for no reply.
2. Handle every teammate message since your last answer, by intent:
   - `question`: answer it when the charter, the code or an earlier decision settles it; a choice
     that belongs to the user goes to `ask_user` first. Reply with `app_runs` Submit into the
     sender's branch, intent `Answer`, mode Send: it wakes the teammate.
   - `blocker`: unblock it — answer, adjust the scope, or move the work to another teammate. A
     change to scope, owned paths or a contract is a decision (below).
   - `decision` proposed by a teammate: accept or reject it as a decision.
   - `status`: nothing to send. Note it for the status table.
   - `done`: check the deliverable against its definition of done — read the changes, run the
     checks the charter names. Short of it, send the teammate what is missing (intent `Answer`).
3. A decision that changes what others build goes to every affected branch as an Aside with
   intent `Decision`: a running teammate reads it at its next step, an idle one in its next turn.
   Record decisions in your answer too, so this branch stays the team's record.
4. When every teammate of a phase is done, start the next phase: one Send per teammate with intent
   `Decision` and what the phase expects of it. After the last phase, integrate: check that the
   parts fit together (build, tests, the contracts at their boundaries), then report.
5. Never edit a teammate's owned paths yourself, and do not send acknowledgements, thanks or
   restatements: every message to a branch costs that teammate a turn.
6. Answer with a short status table (role, state, last report, open question) and what you decided
   or are waiting for. When the work is complete, give the final report instead: what each
   teammate delivered, how it was verified, and what is left.
