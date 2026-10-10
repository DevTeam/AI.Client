---
id: git-conflicts-resolve
name: Git conflicts resolve
icon: wand
aliases: ["conflicts"]
kind: playbook
description: Resolve conflicts in an existing merge, rebase, cherry-pick or revert, stage resolved files and continue the authorized operation after checks.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill","read_multiple_files","edit_file","write_file"]
---

1. Find the repository root and inspect status, unmerged entries and Git operation metadata.
   If no conflicts or operation exist, say so. Conflicts left by stash apply have no continuation
   command: resolve the files while retaining the stash. Never start a new merge or rebase here.
2. Read each conflicted file's base, ours and theirs plus the surrounding code and commit.
   During rebase ours is the rebased target and theirs is the replayed commit. Keep compatible
   changes from both sides. Treat deletion, renaming and binary conflicts explicitly.
3. Resolve clear conflicts with file editing tools; do not blanket-select a side, skip commits,
   reset, clean or discard unrelated work. For ambiguous intent call `ask_user` with a concrete
   explanation and alternatives, the recommendation first. Dismissal delegates only a resolution
   justified by evidence; if none is safe, or on declined/expired/interrupted, leave it paused.
4. Stage only resolved paths; verify the unmerged index is empty, check for introduced markers
   and run relevant build/tests. Filter tests to affected projects/modules first; use the full
   suite as a final relevant check. Select supported quiet/minimal output that retains failures
   and counts, and set an explicit finite test timeout within the tool limit. Split long suites
   and report timeouts as incomplete. Continue only the operation the user authorized, with its matching
   `--continue` and the existing commit message. If continuation was not authorized, ask once.
   Repeat for further conflicts; never bypass hooks or skip a failing commit. For stash conflicts
   do not run --continue or make a commit: report the resolved working tree and retained stash.
5. Report resolved files, checks and the final HEAD, or the paused operation and remaining conflicts
   with exact continue/abort commands. Never push.
