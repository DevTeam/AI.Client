---
id: git-revert
name: Git revert
icon: history
kind: playbook
description: Undo one or several selected commits with new revert commits, keeping a recovery ref and pausing at conflicts; never rewrites history or pushes.
parameters: {"type":"object","properties":{"commits":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill"]
---

Use `process_run` with git argument arrays in the repository root, never shell interpolation.
Follow project instructions. Never fetch or push unless the user separately requests it.

1. Inspect `git status --porcelain=v1 -uall`, the current branch and HEAD, and any operation
   already in progress. Preserve all existing changes, including staged and untracked files.
   Do not start a new operation during a merge, rebase, cherry-pick or revert. Before history
   changes require a clean working tree and index; offer `git-stash` for local work instead of
   silently stashing, resetting, cleaning or discarding it.
2. Take exact commits from the user or call `ask_user` with `pickerKind: "commit"`, the
   absolute `repositoryPath`, `multiSelect: true`, `options: []` and `allowOther: false`.
   Read hashes from `answers[].values`; validate and inspect each commit and its parents.
   Recommend dependent commits be reverted newest first; show and agree the exact order rather
   than silently reversing the picker order. Merge commits require a chosen mainline parent
   and an explanation of how reverting a merge affects future merges.
   Ask only for a missing decision or an operation the user has not authorized; use
   "Proceed (Recommended)" first. A declined, expired or interrupted question changes nothing.
   If a required branch or commit is still missing after dismissal or a partial answer, stop
   and report it; never choose an arbitrary target. An explicitly authorized exact operation
   needs no second confirmation.
3. Record the original HEAD and create a unique backup ref under `refs/backup/ai-client/`
   with `git update-ref` and an expected absent old value; verify it points to the original HEAD.
   Keep this ref for recovery. Never overwrite an existing backup.
4. Run `git revert --no-edit <hash>...` in the agreed order. Handle merge commits separately
   with the explicitly chosen `-m` parent. Never use reset or amend as a substitute for revert.
5. If conflicts occur, do not invent a resolution: report the files and operation state and
   offer `git-conflicts-resolve`; leave the operation paused with the matching `--continue`
   and `--abort` commands. Do not skip commits or silently abort.
6. On success inspect status, history and the resulting diff; run the relevant build/tests.
   Finish with the branch and resulting HEAD, checks and recovery ref, or the precise paused state.
