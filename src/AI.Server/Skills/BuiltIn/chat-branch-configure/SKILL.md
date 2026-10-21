---
id: chat-branch-configure
name: Chat branch configure
icon: git-branch
aliases: ["branch"]
kind: playbook
description: Show which model, reasoning effort, tool approval mode and tool rules a branch runs with and where each comes from, override them for this branch, or make the branch inherit them again; use when the user asks about or changes settings of the current branch or another branch.
parameters: {"type":"object","properties":{"branch":{"type":"string","description":"Branch title or id named by the user; omitted means the current branch"},"connection":{"type":"string","description":"Existing connection name or id to run the branch on, or 'inherit'"},"reasoningEffort":{"type":"string","enum":["none","minimal","low","medium","high","xhigh","max","inherit"],"description":"Chat Completions reasoning effort for a compatible branch model, or 'inherit'"},"approvalMode":{"type":"string","enum":["Ask","Auto","FullAccess","inherit"],"description":"Tool approval mode for the branch, or 'inherit'"},"inheritAll":{"type":"boolean","description":"Drop every override of the branch, tool rules included, only when the user asked for it"}},"additionalProperties":false}
tools: ["app_read","app_chats","app_security","ask_user"]
---

A branch inherits its connection (model), reasoning effort, tool approval mode and tool rules from its parent branch,
nearest first, and the main branch's values are the chat's own. Chat reasoning effort follows the
selected connection unless the chat sets an override. A value set on a branch overrides
the parent for that branch and for every branch below it that does not set its own. Schedules are
never inherited: each branch has its own (`app_schedule`, the `chat-schedule-*` skills).

1. Resolve the branch: the current one when none is named; otherwise match the exact title or id
   among the chat's branches from `app_read` resource=Chat. When several titles match, ask with
   `ask_user` which one; a dismissed or unanswered question changes nothing.
2. Read `app_read` resource=Chat with that `branchId`. Its `effectiveBranchSettings` gives the
   effective `connectionId`, `reasoningEffort` and `approvalMode` with their source fields ("this
   branch", a branch title, or "chat"), and `toolPolicyOverrides`; the branch's own values are in
   `branches[].settings`. Read `app_read` resource=Settings for connection names. When the user only
   asked what the branch runs with, answer from this and stop: name each value, say whether the
   branch sets it or inherits it and from where, and how many tool rules it sets itself.
3. Work out the change from the parameters or the request. Never invent a value: ask with `ask_user`
   for a missing connection or mode, listing enabled connections by name and the three modes in
   words (Ask for approval, Approve for me, Full access), with "Inherit from <source>" as the first
   option. A connection must be enabled. "Inherit", "follow the parent", "reset" or "as before"
   means `inherit` for that field; resetting everything means `inheritAll` true.
   For reasoning effort, use only a value the connection's endpoint supports. If support is
   unknown, explain that the provider may reject the request and prefer inheritance.
4. Loosening approvals needs the person's word: a change to Full access, or from Ask to Approve for
   me — directly or because inheriting brings a looser mode — is applied only when the user asked
   for exactly that in this conversation. Otherwise ask once with `ask_user` (answerStyle='buttons'), saying that tool
   calls on this branch and the branches below it that inherit will run without asking (or after
   only a risk check); anything but a clear yes leaves the mode unchanged. Never loosen approvals to
   avoid a pending approval card. Making approvals stricter needs no confirmation.
5. Apply with `app_chats` operation `SetBranchSettings`, `projectId`, `chatId`, `branchId`, a fresh
   `operationId` and `branchSettings` holding only the fields that change: `connection` (a
   connection id or `inherit`), `reasoningEffort` (a supported value or `inherit`),
   `approvalMode` (`Ask`, `Auto`, `FullAccess` or `inherit`), or
   `inheritAll` true. On the main branch the values are the chat's own and are inherited by every
   branch: there `connection` `inherit` follows the project default, `reasoningEffort`
   `inherit` follows the selected connection, and `approvalMode` needs a value.
   For one tool's rule on this branch use `app_security` `SetBranchToolPolicy` (a `toolPolicy` with
   the exact `serverId`, `name` and `schemaHash` from `app_read` resource=McpTools, decision, limits)
   or `RemoveBranchToolPolicy` to inherit it again; never give `app_security` itself `Allow`. Command
   rules (by the command `process_run` runs or the paths a file tool changes) have no branch level:
   say that a chat rule applies on every branch of the chat, and for one use `app_security`
   `SetCommandRule` with scope `Chat` only when the user accepts that. Check
   `applied` and `error`; on an uncertain result read before retrying with the same `operationId`.
6. Read the branch again with `app_read` resource=Chat and its `branchId` and report in one or two
   lines in the user's language: each changed value, whether it is now set on the branch or
   inherited and from where, and that branches below it which set nothing follow it. No ids.
