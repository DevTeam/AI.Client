---
id: git-commit-message-suggest
name: Git commit message suggest
icon: file-text
kind: playbook
description: Suggest a commit message from the selected changes or commits in the repository's style; changes nothing.
parameters: {"type":"object","properties":{"scope":{"type":"string","enum":["staged","working-tree","commits"]},"commits":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}
tools: ["read_text_file","grep_files","process_run","ask_user","run_skill"]
---

1. Find the repository root. With `process_run` read status, the recent commit log and project
   contribution instructions. Follow the repository's language and message convention.
2. Prefer the staged diff when present; otherwise inspect the working-tree diff and relevant
   untracked files without staging anything. If the user wants a message for existing commits
   and has not named them, call `ask_user` with `pickerKind: "commit"`, the absolute
   `repositoryPath`, `multiSelect: true`, `options: []` and `allowOther: false`.
   Read full hashes from `answers[].values` and inspect each with `git show`. Validate
   supplied revisions as commits using `rev-parse --verify --end-of-options`.
3. Draft a concise imperative subject (normally at most 72 characters), and a body explaining
   why when useful. Do not claim changes or checks unsupported by the diff. Do not add trailers
   unless project instructions require them. If no changes or selected commits exist, say so.
4. The final answer contains the complete proposed message in a copyable code block. Never
   stage, commit, amend or push. A dismissed, declined, expired or interrupted commit selection
   produces no guessed selection; report that no commits were selected.
