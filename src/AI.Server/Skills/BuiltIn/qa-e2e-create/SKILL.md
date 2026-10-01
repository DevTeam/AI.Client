---
id: qa-e2e-create
name: Qa e2e create
icon: qa-e2e-create
kind: playbook
description: Add deterministic end-to-end tests for complete user journeys using the existing framework and isolated fixtures, then execute and report them.
parameters: {"type":"object","properties":{"scenarios":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"The user journeys to automate"},"environment":{"type":"string","description":"The configured test target"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories explicitly supplied or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user","tool_search","run_skill","write_file","edit_file","create_directory","move_file","delete_file"]
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

The request authorizes the specified test/fixture edits. Do not ask for a redundant blanket
confirmation. Show a concrete proposal before a new framework/dependency, breaking contract or
broader scope. Keep product behavior unchanged; route an actual product fix to `code-bug-fix`
only when the user requests it. Never stage, commit, push, deploy or alter live settings.

4. Identify the existing E2E framework, runners, supported browsers and fixtures. Select critical
   journeys spanning actual UI/API/storage boundaries with approved expected behavior.
   Coordinate any test-target selection using ordinary ask_user environment options.
5. Reuse stable semantic/test selectors, isolated accounts/data, bounded condition-based waits
   and existing setup/teardown. Avoid arbitrary sleeps, brittle styling selectors, dependent
   test order or mocks that remove the integration being tested. Keep credentials external.
6. Implement tests/configuration only within the requested scope. A test failure caused by
   product behavior is evidence to report, not a reason to change expected results or product
   code. A new framework/package requires a concrete justified proposal before installation.
7. Run the new scenarios and relevant suite against the exact authorized test environment.
   Follow the same isolated-data and consequential-action rules as `qa-ui-test`; retain traces/
   screenshots with redaction when useful. Check cleanup and repeat only when new evidence
   or a suspected order/timing issue justifies it.
8. For every scenario/check distinguish passed, failed, blocked and not run; pending/running is
not passed. Attach the expected/actual outcome and evidence to the actual revision/environment.
Read exit codes and test counts: a runner that found zero tests or could not start is not a pass.
Do not suppress failures, disable tests, replace assertions with snapshots blindly or claim
complete coverage from absence of findings. Compare final status with the baseline and remove
only task-created scratch artifacts; preserve unrelated work.

9. Report automated journeys and files, actual commands/counts, environment/revision, defects,
   blocked execution and remaining unautomated coverage. Do not confuse unit mocks with E2E.
