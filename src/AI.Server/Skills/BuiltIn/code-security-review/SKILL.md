---
id: code-security-review
name: Code security review
icon: code-security-review
kind: playbook
description: Review selected code for authorization, trust boundaries, input validation and secret handling, reporting supported vulnerabilities and mitigations; changes nothing.
parameters: {"type":"object","properties":{"focus":{"type":"string","description":"Threats, entry points or security requirements to review"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
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

3. Identify protected assets, user roles, trust boundaries and entry points from the code.
   Trace untrusted input to sensitive operations and privileged data. Read security policy,
   authentication/authorization wiring and configuration relevant to the selected scope.
4. Check authorization at resource boundaries, injection and process arguments, path traversal/
   link handling, output encoding, SSRF, upload/archive handling, secrets, sensitive logging,
   insecure defaults and crypto usage when applicable. Distinguish an exploitable path from
   a suspicious pattern by tracing actual validation, permissions and deployment assumptions.
5. Use safe local inspection and the project's configured read-only audit tools when available.
   Do not probe live services, contact attacker-controlled endpoints, execute exploit payloads
   or expose secret values. Redact sensitive evidence and identify its location instead.
   Registry-backed advisory checks require permitted network access; identify the check and
   date, or report that advisories were not verified.
6. For each supported finding show severity, attack prerequisites, affected source/line,
   the path through the trust boundary, impact and a specific mitigation. State uncertainty
   and avoid inflated severity unsupported by the deployment context.
7. The final answer contains findings and mitigations in full, the scope and methods used,
   and validation limits. No findings means no supported issue within this review, not a
   security guarantee. Apply changes only when separately requested.
