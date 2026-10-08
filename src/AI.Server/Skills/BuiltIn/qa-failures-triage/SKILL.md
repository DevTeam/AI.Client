---
id: qa-failures-triage
name: QA failures triage
icon: qa-failures-triage
kind: playbook
description: Investigate test failures and flakiness with bounded isolated reruns, distinguish product, test and environment causes and report evidence; never disables tests.
parameters: {"type":"object","properties":{"failures":{"type":"string","description":"The failing tests, run identifiers or diagnostics"},"environment":{"type":"string","description":"The authorized test target"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories explicitly supplied or selected"}},"additionalProperties":false}
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

4. Read complete failure output, traces, runner configuration, environment/build and recent
   changes. Find the first causal failure instead of counting downstream assertions.
   Group by common symptom while keeping each test's distinct evidence.
5. Reproduce with the same inputs/version; compare isolated and suite runs when order/state/
   timing is implicated. Use bounded repetitions and record every attempt, seed and result.
   Control known sources of nondeterminism through existing test configuration; do not change
   product/test code, insert sleeps or disable parallelism globally merely to make a run green.
6. Distinguish deterministic product defects, incorrect test expectations/fixtures, timing/
   synchronization issues and environment/network/tool failures. A passing retry does not
   erase the original failure or prove it was harmless. Report a measured failure frequency
   only with the actual sample size and conditions.
7. For every scenario/check distinguish passed, failed, blocked and not run; pending/running is
not passed. Attach the expected/actual outcome and evidence to the actual revision/environment.
For automated reruns, filter to the failing test first; expand to the suite only when order or
shared state is under investigation or for a final relevant check. Select runner-supported
quiet/minimal output or a concise reporter that retains failures and counts. Set an explicit
finite timeout within the tool limit; split long suites and mark timeouts incomplete. Read exit
codes and test counts: a runner that found zero tests or could not start is not a pass.
Do not suppress failures, disable tests, replace assertions with snapshots blindly or claim
complete coverage from absence of findings. Compare final status with the baseline and remove
only task-created scratch artifacts; preserve unrelated work.

8. Report grouped causes or ranked hypotheses, reproduction commands/attempts, evidence,
   ownership of the suspected fix and next discriminating checks. Never skip/quarantine tests
   or suppress a failing exit here. An authorized fix uses the matching code/CI skill.
