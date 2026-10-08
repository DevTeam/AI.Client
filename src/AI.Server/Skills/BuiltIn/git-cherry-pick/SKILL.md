---
id: git-cherry-pick
name: Git cherry pick
icon: git-commit
kind: playbook
description: Apply one or several selected commits to the current branch in the chosen order, keeping a recovery ref and pausing at conflicts; never pushes.
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
2. Use the exact commits supplied by the user, or call `ask_user` with `pickerKind: "commit"`,
   the absolute `repositoryPath`, `options: []`, `allowOther: false`, `multiSelect: true`.
   Read hashes from `answers[].values` in selection order. Inspect each diff and parents,
   validate each revision as a commit with `rev-parse --verify --end-of-options` and show the
   proposed application order. Explain dependencies if the chosen order is not suitable;
   do not silently reorder. Merge commits need an explicitly chosen mainline parent.
   Ask only for a missing decision or an operation the user has not authorized; use
   "Proceed (Recommended)" first. A declined, expired or interrupted question changes nothing.
   If a required branch or commit is still missing after dismissal or a partial answer, stop
   and report it; never choose an arbitrary target. An explicitly authorized exact operation
   needs no second confirmation.
3. Record the original HEAD and create a unique backup ref under `refs/backup/ai-client/`
   with `git update-ref` and an expected absent old value; verify it points to the original HEAD.
   Keep this ref for recovery. Never overwrite an existing backup.
4. Run `git cherry-pick <hash>...` in the agreed order. Apply merge commits separately with
   the agreed `-m` parent. If a commit becomes empty, explain why and ask before skipping it.
5. If conflicts occur, do not invent a resolution: report the files and operation state and
   offer `git-conflicts-resolve`; leave the operation paused with the matching `--continue`
   and `--abort` commands. Do not skip commits or silently abort.
6. On success inspect status, history and the resulting diff; run the relevant build/tests.
   Filter tests to affected projects/modules first; reserve the full suite for a final relevant
   check. Use supported quiet/minimal runner output that retains failures and counts, and set an
   explicit finite test timeout within the tool limit. Split long suites; report timeouts as incomplete.
   Finish with the branch and resulting HEAD, checks and recovery ref, or the precise paused state.
