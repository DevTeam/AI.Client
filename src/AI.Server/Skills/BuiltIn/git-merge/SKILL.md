---
id: git-merge
name: Git merge
icon: git-merge
kind: playbook
description: Merge a chosen branch into the current branch, creating a merge commit when needed, keeping a recovery ref and pausing at conflicts; never pushes.
parameters: {"type":"object","properties":{"branch":{"type":"string","description":"Source branch"}},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill"]
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
5. If conflicts occur, do not invent a resolution: report the files and operation state and
   offer `git-conflicts-resolve`; leave the operation paused with the matching `--continue`
   and `--abort` commands. Do not skip commits or silently abort.
6. On success inspect status, history and the resulting diff; run the relevant build/tests.
   Finish with the branch and resulting HEAD, checks and recovery ref, or the precise paused state.
