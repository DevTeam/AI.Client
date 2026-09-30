---
id: code-changes-review
name: Code changes review
icon: diff
kind: playbook
description: Review the uncommitted changes before a commit: summarize them, find stray files, debug leftovers, unrelated edits and likely bugs, and clean up only what the user approves.
parameters: {"type":"object","properties":{"base":{"type":"string","description":"Branch or commit to compare with instead of the last commit, if the user named one"}},"additionalProperties":false}
tools: ["list_directory","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","edit_file","delete_file","process_run","ask_user"]
---

1. Find the repository root among the granted directories; without `.git`, say that this skill
   needs a git repository and stop.
2. With `process_run` and `git` in the root, run `["status","--porcelain=v1","-uall"]`, then
   `["diff","--stat"]` and `["diff","--stat","--cached"]` (with `base`: `["diff","--stat",base]`).
   Read the diff of each changed file with `["diff","--",path]` or `["diff","--cached","--",path]`,
   and read untracked files in full unless they are large or binary.
3. Check every path and hunk for:
   - stray files: scratch or debug scripts, logs, dumps, `*.orig`, `*.rej`, `*.bak`, temp output,
     editor or OS files, local settings, build output that `.gitignore` should cover;
   - secrets: keys, tokens, passwords, connection strings, `.env` files. Never repeat their values;
   - leftovers: debug prints and logging, commented-out code, temporary TODOs, disabled or focused
     tests (`skip`, `only`, `[Ignore]`), hard-coded local paths;
   - unrelated edits: whitespace-only or line-ending changes, reformatting, files outside the task;
   - gaps: new code without tests, new files referenced but untracked, obvious bugs in the hunks.
4. The final answer contains, in full: a summary of what the changes do in two or three sentences;
   the changed files as links with one line each, marking untracked, staged and deleted ones; then
   a "Problems" list with path, line and what is wrong, or a line saying none were found.
5. When problems were found, call `ask_user` with one `multiSelect` question labelled "Clean up": one
   option per fixable item (deleting a stray untracked file, removing a leftover with `edit_file`).
   Dismissed cleans up every item; expired or interrupted changes nothing. Only change what was
   picked. Never revert tracked files with `git checkout`, `restore`, `reset` or `clean`, and never
   stage or commit. Afterwards add one line naming what was cleaned.
