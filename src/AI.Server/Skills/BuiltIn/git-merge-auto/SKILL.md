---
id: git-merge-auto
name: Git merge auto
icon: git-merge-auto
kind: playbook
description: Merge a chosen branch into the current branch, creating a merge commit when needed, keeping a recovery ref and resolving compatible conflicts automatically; never pushes.
parameters: {"type":"object","properties":{"branch":{"type":"string","description":"Source branch"}},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill","read_multiple_files","edit_file","write_file"]
---

Use `process_run` with git argument arrays in the repository root, never shell interpolation.
Follow project instructions. Never fetch or push unless the user separately requests it.

1. Inspect `git status --porcelain=v1 -uall`, the current branch and HEAD, and any operation
   already in progress. Preserve all existing changes, including staged and untracked files.
   Do not start a new operation during a merge, rebase, cherry-pick or revert. Before history
   changes require a clean working tree and index; offer `git-stash` for local work instead of
   silently stashing, resetting, cleaning or discarding it.
2. Take the source/target branch from the user's exact request when provided. Otherwise call
   `ask_user` with `pickerKind: "branch"`, `repositoryPath` set to the absolute repository root,
   `options: []`, `allowOther: false`, and `multiSelect: false`. Read the full ref from
   `answers[].values`. This chooses the branch to integrate into the CURRENT branch, not a
   checkout. Resolve the chosen ref to its commit hash immediately before use and inspect the
   commits and diff involved. Show the current branch, chosen branch and consequences.
   Ask only for a missing decision or an operation the user has not authorized; use
   "Proceed (Recommended)" first. A declined, expired or interrupted question changes nothing.
   If a required branch or commit is still missing after dismissal or a partial answer, stop
   and report it; never choose an arbitrary target. An explicitly authorized exact operation
   needs no second confirmation.
3. Record the original HEAD and create a unique backup ref under `refs/backup/ai-client/`
   with `git update-ref` and an expected absent old value; verify it points to the original HEAD.
   Keep this ref for recovery. Never overwrite an existing backup.
4. Run `git merge --no-edit <resolved-source-hash>` using the repository's normal
   fast-forward policy. Do not force a squash, unrelated histories or a strategy the user did
   not request. Respect hooks and repository message conventions.
5. If conflicts occur, inspect `git status`, unmerged index entries and each file's base,
   ours and theirs, as well as the surrounding code and the affected commit. During rebase
   "ours" is the rebased target and "theirs" is the commit being replayed. Preserve both intents
   when they are compatible. Never use a blanket `-X ours`, `-X theirs`, whole-tree checkout,
   `reset --hard`, or `--skip` to make conflicts disappear. Handle deletes, renames and binary
   files explicitly. Use file editing tools for a reasoned resolution; stage only resolved paths.
   Ask the user when intent is ambiguous, showing the alternatives and recommended resolution.
   If declined, expired, interrupted or still unresolved, leave the operation paused and report
   exact conflicts and the matching `--continue` / `--abort` commands.
6. Verify no unmerged entries or introduced conflict markers remain; run the relevant build/tests
   before continuing, and again after completion. Filter tests to affected projects/modules first;
   reserve the full suite for a final relevant check. Use supported quiet/minimal runner output
   that retains failures and counts. Set an explicit finite test timeout within the tool limit;
   split long suites and report timeouts as incomplete. Use a noninteractive Git editor only to accept
   the existing message when continuing. Repeat for each conflicted commit. Never bypass hooks
   or report success after a failed command. Finish with the resulting branch and HEAD, checks,
   resolutions made and the recovery ref, or the precise paused state.
