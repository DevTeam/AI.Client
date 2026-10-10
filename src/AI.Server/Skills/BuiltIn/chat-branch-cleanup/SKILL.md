---
id: chat-branch-cleanup
name: Chat branch cleanup
icon: git-branch
kind: playbook
description: Delete abandoned branches of this chat that the user picks; the main and current branches are never offered.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read","ask_user","app_chats"]
---

1. Take `projectId`, `chatId` and `branchId` from output.context and read the chat with `app_read`
   resource=Chat; keep its `revision` and branches. The main branch has the chat's own id and the
   current branch is output.context.branchId: offer neither.
2. With no other branches, say so and stop.
3. Call `ask_user` with one `multiSelect` question labelled "Delete": one option per branch, its
   title as label and what it tried plus when it was last updated as description, oldest first,
   with `allowOther` off. Dismissed, expired or interrupted: delete nothing.
4. For each chosen branch, `app_chats` DeleteBranch with dryRun true first, then dryRun false with
   the revision the rehearsal reported. Use each result's revision for the next.
5. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
6. Answer with one line: how many branches were deleted, and whether the chat was kept.
