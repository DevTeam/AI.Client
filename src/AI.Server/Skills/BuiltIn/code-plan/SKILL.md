---
id: code-plan
name: Code plan
icon: code-plan
kind: playbook
description: Plan a code change by researching the existing implementation, comparing approaches and defining files, steps and acceptance checks; changes nothing.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"The change to plan"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user"]
---

The user's instructions take precedence over this playbook. Follow AGENTS.md, CLAUDE.md,
CONTRIBUTING and the repository's build, test and style conventions. Ask questions and report
results in the user's language. Use `process_run` with an executable and argument arrays, not
interpolated shell commands. Directory grants and ordinary tool approvals still apply.

1. Establish the scope from the user's request and `paths`, or infer it from named symbols,
   diagnostics and project structure. Search before reading. Ask only when the scope is genuinely
   ambiguous: use `ask_user` with `pathKind: "file"` for one file or `pathKind: "directories"`
   for modules/directories, `options: []` and `allowOther: false`. Read a file from
   `answers[].other` and directories from `answers[].paths`. Verify selected paths are inside
   granted directories; a picker does not grant access. Do not ask again for values already given.
   For choices with options put the recommendation first with " (Recommended)". Declined stops
   the work concerned; expired or interrupted leaves that decision unresolved. Dismissed permits
   a justified conventional choice, which must be stated; never invent a missing target.
2. Read relevant project instructions, source, callers and tests. If there is Git, record
   `git status --porcelain=v1 -uall` in the repository root. Existing modifications are the
   user's work: preserve them, and edit such files only as needed for this authorized task.
   Never discard or overwrite unrelated changes.
Keep this skill read-only: do not edit source, install dependencies, run migrations, stage,
commit, checkout, stash, reset or push. Use investigation commands that do not mutate application
data or repository files. Report any unavailable evidence instead of treating it as verified.

3. Clarify the desired behavior and acceptance criteria from `goal` and the conversation.
   Trace the current flow and identify integration points, compatibility constraints, existing
   abstractions and relevant tests. Separate verified facts from assumptions.
4. Compare alternatives only where they materially differ in behavior, effort, dependencies
   or compatibility. Recommend the smallest approach that satisfies the request. For a decision
   owned by the user, show concrete alternatives and use `ask_user`; continue independent
   investigation while a choice is unresolved.
5. Produce an ordered implementation plan with affected files/symbols, what changes in each
   step, meaningful verification and any migration or rollback needed. Identify dependencies
   between steps, open decisions and what constitutes completion. Do not promise an estimate
   unsupported by the investigation. When the plan splits into substantial parts that can run at
   the same time on disjoint files, say in one line that team-assemble could carry it out as a team.
6. The final answer contains the complete plan, the recommendation and its rationale, links to
   the relevant source, acceptance checks and unresolved questions. Do not implement the plan
   unless the user requests implementation; that request then belongs to `code-feature-implement`.
