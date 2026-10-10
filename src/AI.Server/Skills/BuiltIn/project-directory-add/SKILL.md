---
id: project-directory-add
name: Project directory add
icon: folder-plus
kind: playbook
description: Grant the current project access to more directories, choosing read-only or read-write access.
parameters: {"type":"object","properties":{"paths":{"type":"array","items":{"type":"string"},"description":"Absolute directories the user already named"},"access":{"type":"string","enum":["read","readwrite"],"description":"Only when the user already said which access they want"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_security","app_chats"]
---

1. Read the current project with `app_read` resource=Project and keep its `revision` and grants.
2. Ask what is still unknown in one `ask_user` call:
   - no `paths`: "Directories" with `pathKind` "directories": "Which directories should this
     project get access to?";
   - no `access`: "Access" with "Read only (Recommended)" and "Read and write". Recommend
     "Read and write" instead when the user asked to change files there.
   Dismissed takes the recommended access; with no directories chosen, stop and say nothing changed.
3. Skip every path that an existing recursive grant already covers, and say which ones you skipped.
4. For each remaining directory, `app_security` AddDirectoryGrant with the project id, the current
   revision, a fresh grant id, `recursive` true, its final segment as the display name and
   toolNames ["read"] or ["read","write","edit","delete"]. Use each result's revision for the next.
5. File tools see the new access from the next step. Continue whatever task needed the access.
6. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
7. Answer with one line listing the directories and the access, and whether the chat was kept.
