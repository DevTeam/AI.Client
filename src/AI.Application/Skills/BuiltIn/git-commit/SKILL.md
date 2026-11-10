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
   Leave out of the proposal every stray, secret or unrelated path. What remains is the proposal.
3. Draft the message in the style of the log (language, prefix convention, length): a subject of at
   most 72 characters in the imperative, and a body with the why when the change is not obvious. Use
   `message` when given. Add trailers only when the project instructions ask for them.
4. Ask once with `ask_user`, two questions in one call and no list of files yet, because a commit is
   usually the whole proposal:
   - "Files", without `multiSelect`: "All N files (Recommended)" with `value` "all" and the
     recommendation, then "Choose files…" with `value` "choose"; `allowOther` off. The text names
     the count and the shortstat, not the paths.
   - "Message": the text shows the draft subject in `inline code` and, when there is one, a line
     that the body goes with it; options "Commit (Recommended)" and "Change the message". A typed
     answer is the new message.
   Committing every proposed file with the drafted message is therefore the recommended one-click
   answer. A question the person answers with neither a choice nor a word stays theirs to decide:
   take the whole proposal and the draft, and say so in the final line instead of asking again.
   Dismissed, declined, expired or interrupted commits nothing.
5. Only when "Choose files…" was chosen, ask again with per-file `multiSelect`, `allowOther` off and
   every option `recommended` so the proposal starts ticked and is unticked. A question accepts at
   most eight options: with more paths, group them into at most eight options by the directory that
   tells them apart, put the directory in the label and the file count with the names in the
   description, and commit every path of a chosen group. A label holds at most 80 characters, so a
   long path goes into the description with its file name as the label. Dismissed, declined,
   expired or interrupted commits nothing.
6. Stage exactly the chosen paths with `["add","--",path,...]`, never `-A` or `.`, then commit with
   `["commit","-m",subject]` plus `-m` for the body. Never `--amend`, `--no-verify`, `push`, `reset`
   or `rebase`. When a hook fails, fix what it reports, stage again and make a new commit.
7. Answer with one line: the short hash and subject, and any file left uncommitted.
