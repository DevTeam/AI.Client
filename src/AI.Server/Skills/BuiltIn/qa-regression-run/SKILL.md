---
id: qa-regression-run
name: QA regression run
icon: qa-regression-run
kind: playbook
description: Select regression checks from changed files, branches or commits, execute them in the authorized test environment and report coverage gaps and defects.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The authorized test environment"},"branch":{"type":"string","description":"The source branch to assess"},"commits":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"The explicitly selected commits"},"base":{"type":"string","description":"The agreed comparison base"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories explicitly supplied or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","process_run","ask_user","tool_search","run_skill"]
---

For verbose test output, use the `purpose: "chatTemporary"` root from
`list_allowed_directories` for task-only logs or reports when available. Keep the console
summary concise, preserve exit codes and failure counts, and inspect relevant failure excerpts.
Remove task-created scratch files when finished.

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

Execute only against the exact authorized application/environment. Prefer configured test
environments, synthetic accounts and disposable/task-owned data. A request to test does not authorize
deleting real data, charging accounts, sending messages to real recipients or changing live settings.
Prepare any consequential scenario with its target, data, effect, checks and cleanup before asking
for missing authorization. Do not ask again for an already authorized exact scenario. Dismissed,
declined, expired or interrupted authorization does not permit the scenario; mark it blocked.
Do not edit product code/configuration, install tooling, deploy, stage, commit or push in this skill.
Clean up only task-created records/resources using recorded identifiers and authorized cleanup.

4. Determine change scope: staged/unstaged changes and relevant untracked files, selected files,
   branch diff from its agreed merge base, or selected commit patches. Use Git pickers for missing
   sources and clarify an ambiguous base. Inspect historical source using resolved hashes.
5. Map changes to contracts, callers, user journeys, integrations and permission/state behavior.
   Select existing automated checks plus targeted manual/exploratory cases by risk, explaining
   exclusions. A changed-file list alone is not a regression plan.
6. Execute the selected documented checks with the existing runner/UI tools. Use the actual
   build/revision under review; do not claim a checkout's test results validate another historical
   revision. Report missing executable builds or environments as blocked.
7. For every scenario/check distinguish passed, failed, blocked and not run; pending/running is
not passed. Attach the expected/actual outcome and evidence to the actual revision/environment.
For automated runs, filter to the affected project/module/tests first; use the wider or full
suite only for final coverage when relevant. Select runner-supported quiet/minimal output or a
concise reporter that retains failures and counts. Set an explicit finite timeout within the tool
limit; split long suites and mark timeouts incomplete. Read exit codes and test counts: a runner
that found zero tests or could not start is not a pass.
Do not suppress failures, disable tests, replace assertions with snapshots blindly or claim
complete coverage from absence of findings. Compare final status with the baseline and remove
only task-created scratch artifacts; preserve unrelated work.

8. Report changed scope, selected checks and why, per-scenario outcomes, totals, defects,
   untested impacted areas and evidence. Running a small subset is not proof of full regression
   coverage. Requesting a product fix switches to the appropriate code skill.
