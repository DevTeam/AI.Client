---
id: qa-test-data-prepare
name: QA test data prepare
icon: qa-test-data-prepare
kind: playbook
description: Prepare reproducible synthetic test datasets and fixtures for selected cases, validate their setup and define safe task-owned cleanup; never alters live data.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"The cases and data conditions to support"},"environment":{"type":"string","description":"The isolated destination, when data will be loaded"},"seed":{"type":"string","description":"The requested reproducibility seed"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories explicitly supplied or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","process_run","ask_user","tool_search","run_skill","write_file","edit_file","create_directory","move_file","delete_file"]
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

4. Derive required entities, roles, relationships, sizes, boundary/invalid conditions and states
   from actual contracts. Use deterministic identifiers/time/seed where possible. Never copy
   production personal data, credentials or secrets into fixtures.
5. Choose the existing fixture/factory/seeding mechanism. Generate minimal valid normal datasets
   and explicit negative/boundary datasets, labeling expected outcomes. Keep cases independent
   and distinguish invalid input fixtures from states the application cannot legitimately create.
   Use the `purpose: "chatTemporary"` root from `list_allowed_directories` for disposable generated
   inputs when available. Keep reusable fixtures and requested deliverables in their intended
   project or output location; remove task-created scratch data after validation.
6. Write authorized fixture/generator files and validate relationships/schema and repeatability.
   Loading data is a separate state mutation: prepare the exact isolated target, collision rules,
   effects, checks and cleanup, and obtain authorization if not already supplied. Never default
   to production or overwrite existing records because seeding failed.
7. When loading is authorized, record every task-owned identifier/namespace, validate results
   and provide bounded cleanup using those identifiers. Do not issue global truncate, reset or
   broad deletes. Dismissed/declined/expired/interrupted load authorization changes no target data.
8. Report fixture files, seed/data scenarios, actual validation/load results, destination and
   cleanup instructions/resources retained. Do not claim data was loaded merely because files exist.
