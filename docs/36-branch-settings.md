# Branch settings

Status: implemented.

Each branch has an optional `ChatBranchSettings` object in the chat document. It contains
`connectionId`, `approvalMode`, and `toolPolicies`. A missing field inherits from the nearest
ancestor branch that sets it, then from the chat. Clearing a branch field restores inheritance;
a branch that sets nothing stores no settings at all. Run branches therefore inherit the settings
of the branch they forked from. Tool policy fields fall back independently, so an override of one
decision does not erase a parent timeout or limit.

The main branch has no settings object: its values are the chat's own (`ConnectionId`,
`ApprovalMode`, `ToolPolicies`), which every branch inherits. Only its connection can fall back
further, to the project and then the global default.

The effective connection selects the model for a run. The effective approval mode and tool
policies govern its tool calls.

Schedules are not inherited. Each branch has at most one schedule of its own (see
[Scheduled chats](35-scheduled-chats.md)); a scheduled run is a child branch of its owner and
inherits the owner's connection, approval mode and tool policies like any other branch.

## Changing them

| Who | How |
| --- | --- |
| Person, any branch | Branch menu → **Branch settings**; on the open chat, chat menu → **Chat settings** for the main branch |
| Person, the open branch | The composer's approval and connection pickers. On a branch other than the main one they write the branch's own value, and their menus start with **Inherit from …**, which drops it |
| Model | `app_chats` `SetBranchSettings` (connection and approval mode; `inherit` per field or `inheritAll`), `app_security` `SetBranchToolPolicy` / `RemoveBranchToolPolicy`, `app_schedule` for the branch's schedule |
| Model, reading | `app_read` resource `Chat` returns `effectiveBranchSettings` for `branchId` (the current branch by default): the effective connection and approval mode and where each comes from |

`IChatService.ChangeBranchSettingsAsync` applies a change to the stored settings under the chat's
lease, so a person and a tool editing different fields of one branch never undo one another.
The HTTP endpoint `PUT …/branches/{branchId}/settings` replaces the whole object.

## In the UI

The chat menu and the branch menu in the sidebar have the same three groups, split by separators:
what to do with the item (*Pin chat*, *Reviews*), its settings (*Chat settings* or *Branch
settings*, then *Tool permissions*), and clearing it out (*Archive chat*, *Delete all branches* or
*Delete all sub-branches*, *Delete chat* or *Delete branch*, each confirmed in place). A count beside
*Branch settings* is how many of the model and approval mode the branch sets itself; beside *Tool
permissions*, how many tool rules the chat or branch sets; beside a bulk delete, how many branches
it removes. *Chat settings* opens the chat first when another one is open.

The settings dialog lists, for one branch: the model, the tool approval mode, a summary of its tool
permission rules (with **Edit**, which opens the permissions drawer in branch scope) and its
schedule (with **Open**, which goes to the branch and shows the Schedule widget). Each setting has a
chip saying where its value comes from: *This branch* (accent tint and dot) when the branch sets it,
*From …* when it follows an ancestor or the main branch. The first choice of a setting is
**Inherit**, which names the value it would get and from where; **Inherit everything** drops every
override at once, tool permissions included. Changes apply as they are made.

In the composer, a picker whose value is set on the open branch carries the same accent dot, and
its tooltip says whether the value is the branch's own or inherited and from where. The branch menu
counts the branch's own overrides.

## Storage

Branch settings stay in the main chat JSON because branch identity, parentage, and settings are
edited together under one chat revision. This keeps the inheritance tree and its overrides in one
atomic document. Schedules are independent, frequently updated state and live in separate
`<chatId>.<branchId>.schedule.json` files. Run state remains in its own per-branch JSON file.

A branch can coordinate a nested team. Teammates are direct member children of that branch;
their reports and blockers go to that branch's lead. The team roster follows the selected team's
lead, and a branch can simultaneously be a member of its parent team and lead its own team.
