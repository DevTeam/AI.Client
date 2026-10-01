---
id: qa-cases-create
name: Qa cases create
icon: qa-cases-create
kind: playbook
description: Draft traceable test cases with prerequisites, data, steps and expected outcomes, covering meaningful normal, boundary and error behavior; changes nothing.
parameters: {"type":"object","properties":{"requirements":{"type":"string","description":"The behavior or acceptance criteria to cover"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories explicitly supplied or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user","tool_search","run_skill"]
---

The user's instructions take precedence. Follow project instructions, test conventions and
runbooks. Use ordinary permission-checked tools; this skill grants no access. Use `process_run`
with executable/argument arrays, never shell interpolation. Report in the user's language and
redact credentials, personal data and sensitive request/response content.

1. Define the requested scope from the message, parameters, requirements and available evidence.
   Search before reading. Infer exact values when justified instead of asking again.
   If file/module scope is unclear use `ask_user` with `pathKind: "file"` or
   `pathKind: "directories"`, empty `options`, `allowOther: false`; read `other` or `paths`.
   For a missing required Git source use `pickerKind: "branch"` or `"commit"`, the absolute
   `repositoryPath`, empty `options`, `allowOther: false`, and `multiSelect` only when needed;
   read `answers[].values` and resolve supplied revisions with
   `git rev-parse --verify --end-of-options`. Do not ask for files when a Git selection is needed,
   or checkout another revision to inspect it.
2. Establish acceptance criteria, environment, application revision, test accounts/roles and
   requested scenarios/platforms from actual configuration and the user's values. For missing
   consequential choices use ordinary `ask_user` options, at most eight per question, the
   recommendation first with " (Recommended)". Declined stops the affected work; expired/
   interrupted leaves the choice unresolved. Dismissed permits only a justified conventional
   choice, which must be stated; never invent a live target, credentials or a passing result.
   Continue independent preparation and report missing decisions instead of asking repeatedly.
3. Read relevant requirements, source/contracts, existing tests, fixtures and prior results.
   Record `git status --porcelain=v1 -uall` where applicable and preserve all existing work.
   Distinguish historical source from the local checkout and record the evidence's actual revision.
   Use `tool_search` to discover permitted browser/API/testing tools when needed, or the existing
   project runner via `process_run`. Do not assume a browser, external account or framework exists.
   Missing tools/access is a blocked check, not a passed check.

This skill produces a complete report in chat. Do not edit product code, tests or configuration,
install tools, submit external issues, notify others, deploy, stage, commit or push. Safe read-only
queries and existing result inspection are allowed; describe unavailable verification honestly.

4. Read approved criteria, existing cases/tests and the relevant contracts. Identify distinct
   input classes, boundaries, state transitions, roles and failure modes; avoid cases that
   differ only cosmetically or assert private implementation.
5. Write stable case identifiers, criterion/risk references, priority, preconditions/accounts,
   synthetic data, exact actions and observable expected outcomes. Include setup/cleanup
   and dependencies; keep each case independently executable where possible.
6. Cover normal journeys, invalid/empty/extreme input, authorization, repeated actions,
   cancellation/retry and recovery where applicable. Identify which cases suit automation,
   exploratory work or environment-dependent verification.
7. Report all cases in full with coverage mapping, required data/environment and unresolved
   expected behavior. A drafted case is not an executed or passing test.
