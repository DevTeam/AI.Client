# Branch settings

Status: implemented.

Each branch has an optional `ChatBranchSettings` object in the chat document. It contains
`connectionId`, `approvalMode`, and `toolPolicies`. A missing field inherits from the nearest
ancestor branch that sets it, then from the chat. Clearing a branch field restores inheritance.
Run branches therefore inherit the settings of the branch they forked from. Tool policy fields
fall back independently, so an override of one decision does not erase a parent timeout or limit.

The effective connection selects the model for a run. The effective approval mode and tool
policies govern its tool calls. The branch menu shows the source of the connection and approval
mode, lets a person set or reset them, and opens the permissions drawer in branch scope. The
composer shows the effective values for the selected branch.

Branch settings stay in the main chat JSON because branch identity, parentage, and settings are
edited together under one chat revision. This keeps the inheritance tree and its overrides in one
atomic document. Schedules are independent, frequently updated state and live in separate
`<chatId>.<branchId>.schedule.json` files. Run state remains in its own per-branch JSON file.

A branch can coordinate a nested team. Teammates are direct member children of that branch;
their reports and blockers go to that branch's lead. The team roster follows the selected team's
lead, and a branch can simultaneously be a member of its parent team and lead its own team.
