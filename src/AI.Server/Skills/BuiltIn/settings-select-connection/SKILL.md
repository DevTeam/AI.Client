---
id: settings-select-connection
name: Settings select connection
icon: settings
kind: playbook
description: Select an existing enabled Connection as the global default, for the current project or chat, or for the subtask pool, preserving other settings after confirmation.
parameters: {"type":"object","properties":{"connection":{"type":"string","description":"Existing connection name or id specified by the user"},"scope":{"type":"string","enum":["Global","Project","Chat","Branch","Subtasks"],"description":"Where to apply the selection"},"inherit":{"type":"boolean","description":"Restore inheritance for Project, Chat or Branch, only when requested"}},"additionalProperties":false}
tools: ["app_read","app_security","app_projects","app_chats","ask_user"]
---

1. Read Settings, the current Project and current Chat with `app_read`; follow all pages. Explain
   the effective connection: chat override, then project override, then global default. Changing
   the global default does not replace project or chat overrides. Subtasks have a separate pool,
   used only when no explicit connection was requested; several may be selected.
2. Resolve connection by exact id or name and require enabled true. Ask for missing choices in
   one `ask_user`: scope ("Current chat (Recommended)", "Current project", "Global default",
   "Subtasks"; when the run is on a branch other than the main one, also "This branch") and connection (best fit first with " (Recommended)"). For Project/Chat also
   offer "Use inherited connection". For Subtasks use multiSelect and display the complete
   proposed pool, including currently selected members. Do not guess ratings or benchmark
   results; offer settings-review-connections if evidence is needed.
3. Show the exact change and affected scope, explicitly naming global changes. Confirm with
   `ask_user` unless the user already specified the exact scope and selection. Dismissed,
   declined, expired or interrupted leaves settings unchanged. An inherited connection must
   resolve to an enabled entry; if it does not, report the missing configuration first.
4. Re-read the affected scope immediately before writing; use a fresh operationId.
   - Chat: `app_chats` SetEndpoint with projectId, chatId, revision, and connectionId; pass
     null to restore project/global inheritance.
   - Branch: `app_chats` SetBranchSettings with projectId, chatId, branchId and
     `branchSettings.connection` set to the connection id, or `inherit` to follow the parent
     branch again. Branches below it that set no connection of their own follow it.
   - Project: `app_projects` Update with projectId, revision, and connectionId; to inherit
     use useDefaultConnection true instead.
   - Global: `app_security` UpsertConnection for the selected enabled entry with IsDefault true;
     send its current definition as expectedConnection. The Host clears the previous default.
   - Subtasks: call UpsertConnection for each entry whose ForSubtasks flag changes, sending
     its current definition as expectedConnection. Use a fresh operationId for each entry.
   Leave all unrelated fields, Connections, MCP servers, environment metadata and policies untouched.
   On a conflict re-read, check the approved change still applies, and retry once.
5. Verify the saved scope with `app_read`. Answer with one line naming the selected connection
   or inheritance, scope and effective connection; no ids, revisions or credentials.
6. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
