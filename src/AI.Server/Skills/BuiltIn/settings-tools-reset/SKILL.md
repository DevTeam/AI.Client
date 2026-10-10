---
id: settings-tools-reset
name: Settings tools reset
icon: shield
kind: playbook
description: Reset selected MCP tool permission overrides in a requested branch, chat, project or global scope to inherited policies; use when the user requests reset, not recommended configuration.
parameters: {"type":"object","properties":{"scope":{"type":"string","enum":["branch","chat","project","global"]},"servers":{"type":"array","items":{"type":"string"}},"tools":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}
tools: ["app_read","app_security","ask_user","app_chats"]
---

1. Resolve scope from parameters or the user's request; use the current chat if unspecified, and
   the current branch for "this branch" (on the main branch, branch scope is chat scope). Read
   `app_read` resources Settings and, for project/chat/branch scope, Project and Chat (with
   `branchId` for a branch; its own rules are in `branches[].settings.toolPolicies`), following
   every page. Match exact supplied server and tool names to existing policies in that scope.
   With no names, reset all overrides in that scope only when the user requested all; otherwise
   use `ask_user` to request the missing selection. Dismissal or no answer leaves policies unchanged.
   No tool discovery is needed: existing records supply serverId, name and schemaHash, including
   obsolete schemas and disabled servers. Never remove policies from another scope.
2. Work out the fallback before removing each override. A branch inherits its parent branches,
   nearest first, then the chat; chat inherits project then global;
   project inherits global; global falls back to Ask, 56535 calls and 600 seconds. Decision,
   limit and timeout inherit independently, and disabled/denied servers remain denied.
   Reset can increase access: if removing Deny exposes Allow/Ask, or removes tighter limits,
   explain the exact effect and ask only when that increase was not already authorized by the
   user's request. Do not interpret an unanswered question as permission.
3. Re-read the selected scope before writing. For each still-present selected override, call
   `app_security` RemoveBranchToolPolicy (projectId, chatId and branchId), RemoveChatToolPolicy
   (projectId and chatId), RemoveProjectToolPolicy (projectId)
   or RemoveGlobalToolPolicy as appropriate, with its exact serverId, name and schemaHash and a
   fresh operationId per distinct removal. Use these narrow operations, never whole-document
   replacements. Check Applied and Error; stop on refusal. On an uncertain outcome read before
   retrying, reusing operationId for the same payload; do not loop on failures.
4. Read the affected scope again to verify removal. Report reset overrides, inherited decisions
   and limits, unchanged and failed items in the user's language. Call the action Reset, not Delete.
   State that a global reset affects every project/chat inheriting it; narrower overrides remain.
   A branch reset also reaches the branches below it that set no rule of their own.
5. Only when this work ran in a chat and the report above is not needed there: check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches, its whole history and the report above, with
   "Keep this chat (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or
   unanswered question, or any other answer, keeps the chat. On delete, read this chat with `app_read`
   resource=Chat for its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that
   revision, a fresh `operationId` and `dryRun` false; when the application refuses the deletion,
   report that and leave the chat.
