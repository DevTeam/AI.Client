---
id: qa-bug-report
name: Qa bug report
icon: qa-bug-report
kind: playbook
description: Draft a reproducible defect report with expected and actual behavior, environment, minimal steps and redacted evidence; changes nothing and does not submit issues.
parameters: {"type":"object","properties":{"problem":{"type":"string","description":"The observed defect or supplied evidence"},"environment":{"type":"string","description":"The affected build/environment"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories explicitly supplied or selected"}},"additionalProperties":false}
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

4. Collect the symptom, original observations, actual build/environment and authoritative
   expected behavior from requirements/contracts. Distinguish verified reproduction from
   a user-reported observation or suspected defect.
5. Reduce supplied reproduction steps to the minimal sequence supported by evidence. Include
   preconditions/accounts, synthetic data, frequency and affected scope. If reproduction is
   missing, state it and propose exact next checks; do not fabricate execution or logs.
6. Draft a concrete title, severity based on demonstrated impact, expected/actual result,
   numbered steps, environment/revision, evidence references and workarounds when verified.
   Separate severity from business priority and label suspected cause as a hypothesis.
7. Report the complete copyable defect report plus missing evidence. Redact secrets/personal
   data. Do not create an external issue or send messages without an explicit request for that
   destination; preparing a report is not authorization to publish it.
