---
id: project-directory-remove
name: Project directory remove
icon: folder-minus
kind: playbook
description: Revoke the current project's access to directories the user picks from its grants.
parameters: {"type":"object","properties":{"paths":{"type":"array","items":{"type":"string"},"description":"Granted directories the user already named"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_security","app_chats"]
---

1. Read the current project with `app_read` resource=Project and keep its `revision` and grants.
   With no grants, say so and stop.
2. Match `paths` to grants by their canonical root. When nothing was named, or a name matched no
   grant, call `ask_user` with one `multiSelect` question labelled "Revoke": one option per grant,
   the display name as label and "<root> · read only" or "<root> · read and write" as description,
   with `allowOther` off. Dismissed, expired or interrupted: change nothing.
3. Say in the question text when the last grant would go: the project could no longer use files.
4. For each chosen grant, `app_security` RemoveDirectoryGrant with the project id, the current
   revision and its `grantId`. Use each result's revision for the next.
5. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
6. Answer with one line listing the revoked directories, and whether the chat was kept.
