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
2. Before starting and before each major step, read the lead branch with `app_read`
   resource=Messages, `branchId` = the parent branch id, for decisions made since the charter. A decision
   overrides your brief. A message the lead sends into this branch arrives as a new turn or, while
   you work, before your next step.
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
   Make the first change early: as soon as the contracts and one example of the project's style
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
5. When the deliverable meets its definition of done, check it once more against the charter's
   contracts, then send intent `Done` (mode Send) with what changed (paths), how it was verified,
   and anything left open. In worktree mode include the worktree path, branch, base commit, ordered
   commit hashes and clean/dirty status so the lead can integrate the exact result. Do not call
   the result done while assigned changes remain uncommitted.
6. Answer in this branch with the same summary, or with what you are waiting for and why.
