---
id: team-coordinate
name: Team coordinate
icon: workflow
kind: playbook
description: Lead a team from its coordinating branch: answer teammates, record decisions, unblock, track status, integrate.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read","app_runs","app_chats","app_navigate","ask_user","list_directory","search_files","grep_files","read_text_file","read_multiple_files","process_run","run_skill"]
---

The user's instructions take precedence over this playbook, and the user's messages in this branch
come first. Ask questions and report results in the user's language. This is the coordinating branch of a
team assembled by team-assemble; its "Team charter" message names the teammates, their branches,
owned paths, deliverables and the protocol (docs/34-asides-and-team-messages.md).

1. Take `projectId` and `chatId` from output.context. This branch is your context: the charter and
   every message a teammate sent you are already in it, so never read this branch with
   `app_read`. Each turn starts with the application's "Team status": every teammate's state, its
   latest report and any question or blocker still waiting for you — that is the team's state, and
   nothing has to be looked up to know it. A message from a teammate starts with a header naming
   its identity ("Ada · Backend"), its branchId and its intent; asides are information that asked
   for no reply. Name every teammate by that identity — in answers, decisions, phase plans and the
   status table — never by a paraphrase of its role.
   A teammate's report is its result: take it as stated. Do not read a teammate's branch to learn
   what it did, to confirm a report, or to see whether it is still working.
2. Handle every teammate message since your last answer, by intent:
   - `question`: answer it when the charter, the code or an earlier decision settles it; a choice
     that belongs to the user goes to `ask_user` first. Reply with `app_runs` with `operation` `Submit` into the
     sender's branch, intent `Answer`, mode Send: it wakes the teammate.
   - `blocker`: unblock it — answer, adjust the scope, or move the work to another teammate. A
     change to scope, owned paths or a contract is a decision (below).
     A `blocker` that starts with "Failed:" is the application reporting that the teammate's turn
     failed (an endpoint or stream error, not the teammate's judgement): resume that branch once
     with `app_runs` with `operation` `Resume`; if it fails again, tell the user which branch is stopped and why.
   - `decision` proposed by a teammate: accept or reject it as a decision.
   - `status`: nothing to send. Note it for the status table.
   - `done`: check the deliverable itself against its definition of done — the files it names
     and the checks the charter names, run once. In worktree mode inspect it in the teammate's
     assigned worktree and verify the reported commits, clean status and owned paths against the
     charter's base; do not inspect the lead's checkout as a substitute. Defer checks that need
     another teammate's changes until after integration. Short of the definition,
     send the teammate what is missing (intent `Answer`). Only when the result contradicts the
     report, read that teammate's branch.
3. A decision that changes what others build goes to every affected branch as an Aside with
   intent `Decision`: a running teammate reads it at its next step, an idle one in its next turn.
   Record decisions in your answer too, so this branch stays the team's record.
4. When every teammate of a phase is done, start the next phase: one Send per teammate with intent
   `Decision` and what the phase expects of it. In worktree mode confirm the next phase still
   needs only the common base and fixed contracts; do not send a teammate to implement against
   files that exist only in another worktree. After the last phase, integrate according to the
   charter's workspace mode. In shared-directory mode the teammates' changes are already together;
   check that the parts fit (build, tests, contract boundaries). In worktree mode, verify the
   lead's integration checkout is clean, each reported commit belongs to its assigned branch and
   changes only owned paths, and there are no uncommitted teammate changes. Integrate the exact
   commits in dependency order with `git-cherry-pick` through `run_skill`; the user's worktree
   choice authorized this local integration, not a push. Stop at conflicts or an unexpected diff,
   preserve every worktree and branch, and report the paused state. After successful integration,
   check that the parts fit together (build, tests, contract boundaries) in the integration
   checkout. Remove only clean, integrated team worktrees with `git-manage-worktree`
   (`action=remove`); retain their branches for recovery. For tests, start with filters for the
   affected modules; run a full suite only as a final relevant check.
   Use the runner's supported quiet/minimal output while preserving failures and counts, and set
   an explicit finite timeout within the tool limit. Split long suites and report timeouts as
   incomplete. Teammates never commit or otherwise change the Git state of the shared working
   directory. In worktree mode they commit only on their own branches, and integration creates
   local commits in the lead's checkout. Report those hashes; never push without a separate request.
5. While teammates work, never edit their owned paths yourself. After they finish, integration may
   change those paths in the lead's checkout; never edit their worktrees. Do not send
   acknowledgements, thanks or restatements: every message to a branch costs that teammate a
   turn. Do not poll the teammates'
   branches or runs while they work, and do not collect their results by reading their branches:
   a teammate's report comes to you as a message — before your next step while you are working,
   or as a new turn once you have ended yours. End the turn when you are waiting, and their
   messages wake you. A teammate whose
   turn stopped for lack of progress reports it here as a `blocker` on its own.
6. Answer with a short status table built from the Team status and the messages of this turn
   (teammate as "Name · Role", state, last report, open question) and what you decided
   or are waiting for. When the work is complete, give the final report instead: what each
   teammate delivered, how it was verified, and what is left.
