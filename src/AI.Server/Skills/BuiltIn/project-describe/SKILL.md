---
id: project-describe
name: Project describe
icon: file-text
aliases: ["describe"]
kind: playbook
description: Write or improve the current project's description from its directories, then apply it after confirmation.
parameters: {"type":"object","properties":{"focus":{"type":"string","description":"What the description should stress, if the user said"}},"additionalProperties":false}
tools: ["app_read","list_directory","read_text_file","ask_user","app_projects","app_chats"]
---

1. Read the current project with `app_read` resource=Project and keep its `revision`.
2. Look at the roots of its granted directories with `list_directory`, and read at most three
   short files that say what the project is: README, AGENTS.md, CLAUDE.md or a package manifest.
   Stop reading once you know its purpose; never read secrets or `.env` files.
3. Write one to three sentences, at most 300 characters, in the language of the project's name:
   what it is, what it is for and its main technology. No Markdown, paths or marketing words.
   Put `focus` first when given.
4. Call `ask_user` with one question labelled "Description" whose text shows the draft, with the
   options "Apply (Recommended)" and "Keep current description"; the user may type their own text.
   Dismissed applies the draft; expired or interrupted changes nothing.
5. On apply, `app_projects` Update with only `description`, the project id and revision.
6. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
7. Answer with one line saying whether the description changed and whether the chat was kept.
