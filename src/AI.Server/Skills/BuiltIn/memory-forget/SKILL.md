---
id: memory-forget
name: Memory forget
icon: eraser
kind: playbook
description: Delete memory entries the user wants forgotten, after showing exactly which ones match.
parameters: {"type":"object","properties":{"about":{"type":"string","description":"What to forget, in the user's words"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_memory","app_chats"]
---

1. Take what to forget from `about` or from the user's message. Search with `app_read`
   resource=Memory and that query; also try one or two synonyms.
2. With no match, say so and stop.
3. Call `ask_user` with one `multiSelect` question labelled "Forget": one option per match, the
   title as label and "<scope> · <body>" cut to 100 characters as description, with `allowOther`
   off. Dismissed, expired or interrupted: delete nothing.
4. `app_memory` Delete each chosen entry with its resourceId and revision.
5. Answer with one line: how many entries were forgotten.
6. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
