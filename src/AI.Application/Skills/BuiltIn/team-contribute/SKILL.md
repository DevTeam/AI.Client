---
id: team-contribute
name: Team contribute
icon: users
kind: playbook
description: Work as a teammate in a branch: do the assigned part and report to its parent lead branch.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read","app_runs","ask_user","run_skill","list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","write_file","edit_file","create_directory","process_run"]
---

The user's instructions take precedence over this playbook. Report in the language of the charter.
This branch is one teammate of a team assembled by team-assemble. The "Team charter" message and
the analysis before it are in this branch's history; the brief that started this branch is the
part that is yours. Team messages are described in docs/34-asides-and-team-messages.md.

1. Take `projectId`, `chatId` and `branchId` from output.context. The lead is this branch's parent
   branch; read its id from the branch metadata in `app_read` resource=Chat. From the charter and your brief, restate for yourself: your
   identity ("Ada · Backend"), scope, owned paths, deliverable, definition of done and the
   contracts you depend on. Name yourself, the lead ("Lead") and other teammates only by their
   identities from the charter.
   Read this branch's `effectiveBranchSettings` only when a model or permission affects your task
   or causes a failure. Its connection, approval mode and tool rules may be inherited from the
   parent branch. If a different connection or permission is needed, send the parent lead a
   `Question` or `Blocker` with the task requirement and the narrow change needed; do not broaden
   the whole team's settings yourself.
2. Before starting and before each major step, read the lead branch with `app_read`
   resource=Messages, `branchId` = the parent branch id, for decisions made since the charter. A decision
   overrides your brief. A message the lead sends into this branch arrives as a new turn or, while
   you work, before your next step.
   Keep the last message id you saw on that branch. On later reads pass it as `afterMessageId` and
   page only through new messages; do not repeatedly reread the charter or older decisions. Apply
   new `Decision` messages and remember the newest id even if the page contained no decision. If
   there is no new decision, continue the existing assignment.
3. Do the work. Start each skill your brief names with `run_skill` in this turn and follow its
   steps; with none named, follow the project's conventions and its checks. Change only your owned
   paths. A change anywhere else, or to a contract, is a question to the lead. Read the charter's
   workspace mode before touching files. In worktree mode, verify the assigned absolute worktree
   path and Git branch against `git worktree list --porcelain`, and verify that the charter's base
   commit is an ancestor of the branch. Use that path for every file operation and command.
   Resolve owned repository-relative paths inside that worktree;
   never edit the lead's checkout or another teammate's worktree. If a tool cannot reach the
   assigned path under the project's grants, report a blocker to the lead rather than using a
   process to bypass the grant.
   Make one short execution plan from the brief, then act on it. Do not re-plan the whole team,
   narrate routine tool results, or repeatedly explain your reasoning to yourself. Reconsider the
   plan when a contract, new decision, failed check or unexpected result changes the facts. Give
   uncertain contracts, security-sensitive changes and integration enough analysis to be correct;
   concise communication is not a reason to skip verification.
   Make the first change early, except when you must start a worktree subteam from your clean HEAD:
   create those child worktrees before editing your integration checkout. Otherwise, as soon as
   the contracts and one example of the project's style
   are known, write the smallest real part of the deliverable in your owned paths — the files,
   types and signatures the contracts fix — and grow it from there. Reading on without changing
   anything is not progress: when a few more reads still leave you unable to start, ask the lead
   or report a blocker instead.
   When the charter's contracts, a reference you were pointed to and another teammate's work
   disagree, do not settle it yourself and do not stop for it: send a question at once with the
   exact case and both readings, then carry on by the charter's contract, which holds until the
   lead decides.
   In shared-directory mode, other teammates' unfinished work is on disk next to yours: never
   change the Git index or working tree as a whole — no commit, stash, checkout, reset, restore,
   clean, rebase, merge, cherry-pick or revert, even when another skill's steps say so. Committing
   and integrating are the lead's. In worktree mode, the user's choice authorizes local commits
   for integration: after checking the diff, stage only owned paths with `git add -- <paths>`,
   verify the staged paths, and commit on your assigned branch. Do not change Git state outside
   that worktree, use `git add -A` or `.`, rewrite history, merge, rebase, cherry-pick, push, or
   remove any worktree. Report a hook failure or unexpected status to the lead without bypassing
   it. Read-only Git (status, diff, log) is fine in either mode.
   A question another skill would put to the user goes to the lead first; use `ask_user` only for
   a decision the charter leaves to the user, and say in it that you are asking as <role>.
   If the brief assigns you a subteam, run `team-assemble` from this branch for a decomposition
   strictly inside your owned paths. You remain this branch's member of the parent team and the
   lead of your direct children. In shared-directory mode, do not change the shared Git state.
   In worktree mode, the parent-assigned worktree is your integration checkout: start children
   only from its clean committed HEAD, commit your own disjoint edits before integration so the
   checkout is clean, and integrate their exact commits there with `team-coordinate`. Any later
   edits must also be committed only on your assigned branch. The nested lead integration is
   the sole exception to the teammate prohibition on cherry-picking. Do not report `Done` to the
   parent until every child is done, their changes are integrated and the combined deliverable is
   checked. Report your own ordered branch commits, not the child commits that were cherry-picked.
4. Write to the lead with `app_runs` with `operation` `Submit`, `branchId` = the parent branch id, no `operationId` (the application assigns it),
   `wait` false:
   - progress worth knowing: mode Aside, intent `Status`, at most once per phase;
   - something only the lead or the user can settle: mode Send, intent `Question`; then carry on
     with what does not depend on the answer, or end the turn saying what you wait for. Do not
     poll the lead branch for the answer: it arrives in this branch as a message, and reading the
     same thing again and again stops the turn as making no progress;
   - stuck with no way forward: mode Send, intent `Blocker`, with what you tried;
   - a decision others must follow that your scope lets you make: mode Aside, intent `Decision`.
   The lead reads each message under a header with your identity and its intent, added by the
   application: do not open it with your name, role or status. Every message is self-contained — what, why, and what you need back. No acknowledgements, no
   thanks, and no messages to other teammates: the lead routes them.
   Send only the delta since your last report. For a question, state the exact case, viable options,
   your recommendation and what is blocked. For a status, state completed work, the next step and
   any new risk in a few lines. Do not send raw tool output, a diary of attempts, repeated charter
   text or private reasoning. Include a file path, message id or commit hash when it lets the lead
   verify or act without asking you to repeat the context.
5. When the deliverable meets its definition of done, check it once more against the charter's
   contracts, then send intent `Done` (mode Send) with what changed (paths), how it was verified,
   and anything left open. In worktree mode include the worktree path, branch, base commit, ordered
   commit hashes and clean/dirty status so the lead can integrate the exact result. Do not call
   the result done while assigned changes remain uncommitted.
   At a phase boundary or after a long turn, use your answer in this branch as a compact handoff: current
   result, decisions applied, open question, next action and evidence locations. Omit finished
   exploration and tool transcripts. This is for resuming work after context compaction, not an
   additional message to the lead unless one of those facts changed for the team.
6. Answer in this branch with the same summary, or with what you are waiting for and why.
