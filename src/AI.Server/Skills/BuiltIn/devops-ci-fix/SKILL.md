---
id: devops-ci-fix
name: DevOps CI fix
icon: devops-ci-fix
kind: playbook
description: Diagnose a failed CI run from its logs and configuration, fix the demonstrated pipeline cause and validate the change; reruns remote jobs only when explicitly authorized.
parameters: {"type":"object","properties":{"run":{"type":"string","description":"The failed CI run identifier or URL"},"diagnostics":{"type":"string","description":"The relevant failure output"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user","write_file","edit_file","create_directory","move_file","delete_file","run_skill"]
---

The user's instructions take precedence. Follow repository instructions, deployment runbooks
and the existing toolchain. Use ordinary permission-checked tools; this playbook grants no access.
Use `process_run` with an executable and argument arrays, never interpolated shell commands.
Do not expose secret values in arguments, logs, diffs or the final answer, or ask for secrets in
chat. Use configured credential stores and existing secret references. Report in the user's language.

1. Establish the requested goal and scope from the message and parameters. Search before reading
   instructions, manifests, CI/deployment configuration and relevant application contracts.
   Use `ask_user` with `pathKind: "file"` or `pathKind: "directories"`, `options: []` and
   `allowOther: false` only when the configuration scope cannot be inferred; read the selected
   file from `answers[].other` or directories from `answers[].paths`. Verify granted access.
2. Discover actual environments, services, providers and artifact versions from configuration,
   runbooks and permitted read-only CLI queries. Use values the user already supplied. For missing
   consequential choices call `ask_user` with concrete existing options, the recommendation first
   with " (Recommended)", at most eight options per question; narrow large lists before selection.
   Choose environment, service and release together when interdependent. Never invent an environment
   or default to production. For a required Git branch/commit use `pickerKind: "branch"`/
   `"commit"`, absolute `repositoryPath`, empty `options`, `allowOther: false` and
   `multiSelect` only when needed; read full refs/hashes from `answers[].values` and resolve
   them with `git rev-parse --verify --end-of-options`. Do not checkout to inspect a revision.
   Declined stops the affected work; expired/interrupted or an empty required selection leaves it
   unresolved. Dismissed delegates only a justified low-impact choice; never guess a live target,
   release, destructive action or permission. Continue independent preparation and report what
   could not be chosen rather than repeating the same question.
3. Record `git status --porcelain=v1 -uall` when applicable. Preserve existing changes, including
   untracked configuration. Discover installed CLI versions and read their actual context/account/
   cluster/region/namespace before any live query or mutation. Use explicit targets in commands;
   do not silently switch global contexts, grant access, install tools or upgrade the toolchain.

The request authorizes the specified local configuration edits. Do not ask for a redundant
blanket confirmation. Present a concrete proposal before a new dependency, breaking contract or
materially broader scope. Build/test artifacts may be created when needed for validation. Publishing
artifacts, changing remote settings or live services needs authorization for that concrete action.
Never stage, commit, tag, push, deploy or run database migrations under this preparation skill.

4. Read the specified run's job, revision, matrix, environment, logs and artifacts through
   permitted existing provider tools/CLI. If unavailable, analyze supplied logs and state the
   missing evidence. Sanitize sensitive output. Identify the first causal failure, distinguishing
   infrastructure, restore, test, permission, cache, packaging and artifact-transfer failures.
5. Reproduce the cause locally with equivalent versions/configuration where possible. Compare
   successful runs or recent workflow changes. Application compiler/analyzer repair belongs to
   `code-build-fix`; test/application defects belong to `code-bug-fix` when a fix is authorized.
   Do not repair the pipeline by removing a failing test, accepting nonzero exits, granting broad
   permissions or suppressing diagnostics.
6. Make the smallest demonstrated configuration/script fix. Validate syntax and the affected
   local steps. Rerun only the identified remote job when explicitly authorized, checking the
   provider's current state first; do not create duplicate runs or alter unrelated jobs.
7. Use the existing documented validators, build/test commands, dry runs and safe checks relevant
to the task. For test commands, filter to the affected project/module/tests first and use a full
suite only as a final relevant check. Select supported quiet/minimal runner output that preserves
failure details and counts. Set an explicit finite test timeout within the tool limit; split long
suites and report timeouts as incomplete. Read results and exit codes; distinguish success, failure and unavailable
checks. Never disable security checks, weaken tests or claim a validation that did not run.
Compare final repository status with the baseline and remove only scratch artifacts created by
this task. Never discard unrelated local or remote work.

8. Report the causal failure, changed files, local checks and any actual remote run outcome.
   An inaccessible or still-running CI job is not a passed verification.
