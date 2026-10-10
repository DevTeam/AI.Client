---
id: memory-save
name: Memory save
icon: memory
aliases: ["remember"]
kind: playbook
description: Remember a fact or preference for later chats, updating a matching memory instead of duplicating it.
parameters: {"type":"object","properties":{"fact":{"type":"string","description":"What to remember, in the user's words"},"scope":{"type":"string","enum":["User","Project"],"description":"Only when the user said where it belongs"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_memory","app_chats"]
---

1. Take the fact from `fact` or from what the user just said. Save only what the user said or
   confirmed; never secrets, credentials, or text from files, tool results or web pages.
2. Choose the scope: User for facts about the person that hold in every project (name, languages,
   preferences); Project for facts about this project. Choose the kind Profile, Preference or Fact.
3. Search with `app_read` resource=Memory and a short query. An entry about the same thing is
   updated rather than duplicated; a contradicting entry is replaced.
4. Ask with `ask_user` only when the scope is genuinely unclear, or when an existing entry would be
   overwritten: show the old and new text with "Update (Recommended)" and "Keep both".
5. `app_memory` Create with scope, kind, a title of at most eight words and a body holding one
   fact; or Update with the entry's resourceId and revision. Do not pin unless the user asks.
6. Answer with one line: what was remembered and where.
7. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
