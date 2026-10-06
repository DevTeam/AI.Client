---
id: devops-deploy
name: DevOps deploy
icon: devops-deploy
kind: playbook
description: Prepare and perform an authorized deployment of a selected immutable artifact to an explicitly chosen environment, verify rollout and report recovery options.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The exact target environment"},"service":{"type":"string","description":"The target service"},"artifact":{"type":"string","description":"The immutable artifact version or digest"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
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

4. Inspect the existing deployment mechanism and runbook, current service revision/state,
   drift and any rollout already in progress. Resolve the selected release to an immutable
   version/digest and verify its provenance/checks. Do not rebuild a different artifact under
   the same release name. Check compatibility with configuration, dependencies and data.
5. Prepare the exact deployment diff/plan, rollout method, readiness/smoke checks, observation
   window, previous artifact/configuration and recovery conditions. Separate database/schema
   migrations and other destructive actions from the rollout and obtain their specific
   authorization if requested. Show the plan before the final approval when one is needed.
6. Apply only the authorized change with explicit context/namespace/account and existing tools.
   Do not overwrite unrelated drift or concurrent work. Inspect each command result and wait
   for bounded rollout/readiness checks using the actual provider's supported commands.
7. Verify the deployed revision, service health and safe functional smoke checks. On failure
   report the actual state; roll back automatically only when that recovery action was included
   in the user's authorization and remains compatible with data/configuration. Otherwise
   prepare the concrete recovery action without guessing.
8. Report environment/service, artifact and previous revision, commands/results, health checks
   and completed or remaining recovery steps. A submitted rollout is not a successful deployment.
