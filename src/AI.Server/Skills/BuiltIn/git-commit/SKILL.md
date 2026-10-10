---
id: git-commit
name: Git commit
icon: git-commit
aliases: ["commit"]
kind: playbook
description: Commit the current changes after the user asks to: check for stray files, draft a message in the repository's style, and commit the chosen files after confirmation; never pushes.
parameters: {"type":"object","properties":{"message":{"type":"string","description":"The commit message or its gist, if the user gave one"}},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill"]
---

Run this skill only when the user asked for a commit.

1. With `process_run` and `git` in the repository root, run `["status","--porcelain=v1","-uall"]`,
   `["diff","--stat","HEAD"]` and `["log","--oneline","-10"]`. Nothing to commit: say so and stop.
2. When there are untracked files, debug leftovers or files that look unrelated, or when
   `code-changes-review` has not run on these changes, check the diff for them as that skill does.
   Leave out of the proposal every stray, secret or unrelated path.
3. Draft the message in the style of the log (language, prefix convention, length): a subject of at
   most 72 characters in the imperative, and a body with the why when the change is not obvious. Use
   `message` when given. Add trailers only when the project instructions ask for them.
4. Call `ask_user` with two questions: "Files", a `multiSelect` question with one option per proposed
   path, and "Message", showing the draft with "Commit (Recommended)" and "Change the message"; a
   typed answer is the new message. Dismissed commits the proposal; expired or
   interrupted commits nothing.
5. Stage exactly the chosen paths with `["add","--",path,...]`, never `-A` or `.`, then commit with
   `["commit","-m",subject]` plus `-m` for the body. Never `--amend`, `--no-verify`, `push`, `reset`
   or `rebase`. When a hook fails, fix what it reports, stage again and make a new commit.
6. Answer with one line: the short hash and subject, and any file left uncommitted.
