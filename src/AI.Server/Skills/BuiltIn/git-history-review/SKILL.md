---
id: git-history-review
name: Git history review
icon: history
kind: playbook
description: Review the history and diffs of selected branches or commits; changes nothing.
parameters: {"type":"object","properties":{"branch":{"type":"string"},"commits":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}
tools: ["read_text_file","grep_files","list_allowed_directories","process_run","ask_user","run_skill"]
---

1. Find the repository root and read status and recent history with `process_run`.
2. Take exact branches/commits from the user when given. Otherwise ask what to inspect with
   `ask_user`: use `pickerKind: "branch"` for branches or `pickerKind: "commit"` for commits,
   the absolute `repositoryPath`, `options: []`, `allowOther: false`, and `multiSelect: true`.
   Optional `revision` limits the commit picker to the selected branch. Read full refs/hashes
   from `answers[].values`. Dismissed, declined, expired or interrupted with no selection:
   report no selection and stop.
3. Resolve supplied revisions safely with `rev-parse --verify --end-of-options`; inspect
   selected commits with `git show` and branches with a bounded graph/log and diff. Compare
   two branches from their merge base when relevant; distinguish committed changes from local work.
   For oversized history output, use the `purpose: "chatTemporary"` root from
   `list_allowed_directories` for a task-only copy when available. Read only relevant excerpts,
   preserve command exit codes and remove the scratch copy after review.
4. The final answer contains the requested history review in full, with hashes, subjects,
   significant changes and any evidence-based concerns. Never checkout, stage, commit or fetch.
