---
id: code-explain
name: Code explain
icon: code-explain
kind: playbook
description: Explain selected code, its callers, data flow, design decisions and limitations with source references; changes nothing.
parameters: {"type":"object","properties":{"focus":{"type":"string","description":"The symbol, behavior or question to explain"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
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

3. Locate the entry point for `focus`, then trace actual call sites, dependencies and data/state
   transformations. Include DI registration, configuration and boundaries only when they affect
   the behavior being explained. Read tests as evidence of expected behavior, not proof that
   every path works.
4. Explain from the user's observable behavior down to the implementation. Use a concrete
   example when useful; cover error handling, async/concurrent behavior and important edge
   cases. Distinguish what the code does from an inferred reason why it was designed that way.
5. For a diagram, derive nodes and edges from the inspected code. Cite real files and symbols,
   and line numbers when verified. Do not fabricate APIs, execution results or author intent.
6. The final answer contains the explanation in full, source links, relevant limitations and
   any remaining uncertainty. Recommendations may be included when requested, but are not edits.
