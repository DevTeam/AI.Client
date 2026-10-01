---
id: devops-rollback
name: Devops rollback
icon: devops-rollback
kind: playbook
description: Prepare and perform an authorized rollback to a selected previous release after checking data and configuration compatibility, then verify service recovery.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The exact target environment"},"service":{"type":"string","description":"The service to restore"},"release":{"type":"string","description":"The previous immutable release/revision to restore"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
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

Prepare a concrete, reviewable plan before mutation: exact account/context, environment,
service/resources, immutable artifact or state, commands, expected effects, checks and recovery.
Proceed only when the user's authorization covers that action and target. Otherwise show the
completed plan and request the missing approval with `ask_user`; approval is the final decision,
not an excuse to skip preparation. Do not ask again for an already authorized exact operation.
Declined, expired, interrupted or dismissed approval applies nothing; keep the prepared result.
An authorized source/configuration change never implicitly authorizes destructive replacement,
data deletion, database migration, credential rotation, publishing or another environment.

4. Inspect deployment history, current state and any operation already in progress. Select an
   actual previous release with ordinary ask_user version options when unspecified; never
   assume the immediately previous release is known-good. Verify its artifact is available.
5. Compare configuration, secrets references, dependencies and schema/data changes between
   revisions. A binary rollback does not reverse database migrations. Identify irreversible
   changes and whether a forward fix is safer; do not downgrade a schema or restore data under
   an authorization only to roll back application binaries.
6. Prepare the exact target revision/configuration, commands, downtime expectations, checks and
   fallback. After concrete authorization apply with the existing mechanism and explicit target.
   Preserve the current revision for recovery and do not overwrite unrelated changes or drift.
7. Verify the restored artifact, rollout completion, readiness and the affected behavior using
   bounded safe checks. If recovery fails, report state and evidence rather than repeatedly
   switching versions or restarting services without a new justified plan.
8. Report restored environment/service/release, compatibility decisions, recovery checks and
   any unresolved incident. Do not describe command submission alone as restored service.
