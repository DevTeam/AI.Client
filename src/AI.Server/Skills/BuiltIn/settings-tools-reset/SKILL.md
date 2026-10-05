---
id: settings-tools-reset
name: Settings tools reset
icon: shield
kind: playbook
description: Reset selected MCP tool permission overrides in a requested chat, project or global scope to inherited policies; use when the user requests reset, not recommended configuration.
parameters: {"type":"object","properties":{"scope":{"type":"string","enum":["chat","project","global"]},"servers":{"type":"array","items":{"type":"string"}},"tools":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}
tools: ["app_read","app_security","ask_user"]
---

1. Resolve scope from parameters or the user's request; use the current chat if unspecified.
   Read `app_read` resources Settings and, for project/chat scope, Project and Chat, following
   every page. Match exact supplied server and tool names to existing policies in that scope.
   With no names, reset all overrides in that scope only when the user requested all; otherwise
   use `ask_user` to request the missing selection. Dismissal or no answer leaves policies unchanged.
   No tool discovery is needed: existing records supply serverId, name and schemaHash, including
   obsolete schemas and disabled servers. Never remove policies from another scope.
2. Work out the fallback before removing each override. Chat inherits project then global;
   project inherits global; global falls back to Ask, 65535 calls and 600 seconds. Decision,
   limit and timeout inherit independently, and disabled/denied servers remain denied.
   Reset can increase access: if removing Deny exposes Allow/Ask, or removes tighter limits,
   explain the exact effect and ask only when that increase was not already authorized by the
   user's request. Do not interpret an unanswered question as permission.
3. Re-read the selected scope before writing. For each still-present selected override, call
   `app_security` RemoveChatToolPolicy (projectId and chatId), RemoveProjectToolPolicy (projectId)
   or RemoveGlobalToolPolicy as appropriate, with its exact serverId, name and schemaHash and a
   fresh operationId per distinct removal. Use these narrow operations, never whole-document
   replacements. Check Applied and Error; stop on refusal. On an uncertain outcome read before
   retrying, reusing operationId for the same payload; do not loop on failures.
4. Read the affected scope again to verify removal. Report reset overrides, inherited decisions
   and limits, unchanged and failed items in the user's language. Call the action Reset, not Delete.
   State that a global reset affects every project/chat inheriting it; narrower overrides remain.
