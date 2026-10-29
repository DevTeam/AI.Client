---
id: chat-execution-select
name: Chat execution select
icon: workflow
kind: playbook
description: Choose direct work, subtasks or a team within the current task, including a teammate's assignment.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read","spawn_subtask","run_skill","tool_search"]
---

This is the shared execution policy for working skills and team roles. Apply it to the task
already in progress; choosing an execution method does not replace that task or its active skill.
The user's explicit choice wins. Ordinary directory grants, tool approvals, model availability,
read-only requirements and the team's charter still apply to every worker.
For a team assignment, its role protocol takes precedence over a general working skill for
ownership, reporting and explicitly authorized local worktree commits; it never authorizes a push.

After a short investigation, and when new facts change dependencies, choose the lightest method
that is likely to finish the authorized work sooner after briefing, verification and integration:

- **Direct work:** a small task, tightly coupled edits, a sequential dependency chain, or work
  whose handoff would cost more than it saves. Do not delegate merely because many files exist.
- **Subtasks:** independent bounded assignments whose answers or artifacts can be collected:
  investigate separate hypotheses, compare options, review independent modules, translate sections,
  or implement disjoint pieces against a fixed contract. Use `spawn_subtask` without asking the
  person to choose an execution method. Batch independent assignments in one call; keep a small
  number of useful workers rather than filling every slot. A failed hypothesis is evidence,
  not a reason to duplicate another worker's investigation.
  Start independent investigations before reading all their implementations yourself: give
  workers search targets and collect their findings before doing shared integration work.
- **Team:** at least two substantial assignments can proceed together, ownership does not overlap,
  contracts can be fixed up front, and continuing questions, decisions or phased coordination
  justify persistent teammate branches. Run `team-assemble` without a separate request for a team.
  A large output or several lookups alone normally needs subtasks. A planning or read-only request
  permits only planning or read-only assignments, never implementation of the proposed result.

Before delegation, give each worker its goal, essential context, inputs, deliverable, acceptance
criteria, absolute workspace path, owned write targets and restrictions. Put shared rules in
`sharedInstructions`. Every simultaneous writer, including the caller, has disjoint targets;
shared files have one owner. Do not read another writer's unfinished output as a stable input.
Fix contracts first or keep dependent work sequential. Subtasks cannot obtain an interactive
approval: do the approval-bound part in the calling branch, use an already allowed alternative,
or report the narrow blocker. Never loosen permissions just to enable parallelism.

A teammate may use subtasks autonomously within its assignment, without the lead's permission
for the execution method. Pass down the charter's contracts, owned paths, workspace mode,
absolute assigned worktree when present, and Git restrictions. A teammate branch is not itself
a `spawn_subtask` worker: the existing subtask nesting limit applies when a subtask delegates
again, not merely because its caller belongs to a team. Respect a depth refusal; do the work
directly or report what remains. A subteam is a separate coordination decision handled by
`team-assemble` and the parent lead, not a prerequisite for using subtasks.

The caller owns integration and one combined result. Accept successful worker checks with their
evidence; inspect failures, missing evidence and contradictions, and run checks across the combined
result. Do not repeat the same local checks or reassign work already in progress without a reason.
Finish or account for every delegated assignment before reporting completion. A teammate sends
one integrated report to its lead, including checks and unresolved items, not raw worker transcripts.
Reassess when dependencies or conflicts remove the expected benefit; preserve finished work and
avoid creating a second set of workers for the same assignment.

Briefly state useful parallel assignments as progress, then start them. Do not add a confirmation
solely for the execution method, promise an unmeasured speedup, or let orchestration become the task.
If a required tool is not currently offered, discover its exact name with `tool_search` once.
If it is unavailable after discovery, continue with the available direct work.
