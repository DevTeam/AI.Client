---
id: git-stash
name: Git stash
icon: archive
kind: playbook
description: Save local work in a named stash or apply a selected stash, preserving the stash until restoration is verified; never drops work automatically.
parameters: {"type":"object","properties":{"action":{"type":"string","enum":["save","apply"]},"message":{"type":"string"}},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill"]
---

1. Read status, branch and `git stash list` in the repository root. Check for an in-progress
   operation or unresolved conflicts; do not stash them. Preserve ignored files and other work.
2. Take action from the user or ask with "Save local changes (Recommended)" and "Apply a stash".
   For save inspect tracked/untracked changes, ask whether to include untracked files when present
   unless specified, and choose a descriptive message. Never include ignored files automatically.
   For apply list stashes as ordinary `ask_user` options with hash/message, in batches of at most
   eight if necessary; this is a stash selection, not the commit picker. Record the chosen stash hash.
3. Show the exact action and affected files. Proceed when already authorized; otherwise confirm
   with `ask_user`. Dismissed missing stash/action, declined, expired or interrupted: change nothing.
4. Save with `git stash push -m <message>`, adding `--include-untracked` only when agreed.
   Apply with `git stash apply --index <stash-hash>` to restore the index as well as file changes.
   Never pop, drop, clear or overwrite local work. If there are collisions or conflicts, report them
   and leave the stash intact; offer `git-conflicts-resolve` for resolving the files without an
   operation continuation.
5. Verify status and stash list and report the saved/restored changes, retained stash and conflicts.
