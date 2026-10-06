---
id: devops-backup-verify
name: DevOps backup verify
icon: devops-backup-verify
kind: playbook
description: Check backup freshness, integrity and restoration of selected data in an explicitly isolated test destination, report recoverability; never overwrites live data.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The source environment"},"service":{"type":"string","description":"The data service or backup set"},"backup":{"type":"string","description":"The backup version or identifier"},"destination":{"type":"string","description":"The explicitly isolated restoration destination"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
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


Verification of metadata/integrity is read-only. A test restore creates or changes resources at
its destination: prepare and obtain authorization for that exact isolated destination when it
has not already been given. Never overwrite production, restore over existing user data,
rotate credentials, delete backups or change retention policies in this skill.
4. Inspect backup policy, schedule, retention, source/version, encryption/key references and
   restore runbook. Check actual available backups, freshness and reported completion/integrity;
   a successful upload or checksum alone does not prove restorability.
5. Select the backup and discover its application/database/tool compatibility and dependencies.
   Identify required keys/permissions via configured stores without exposing values. State
   recovery-point/time objectives from the user's/runbook requirements, not invented targets.
6. Prepare a test restore plan with an explicit isolated destination, resource names, network/data
   access, expected contents, checks, cleanup and estimated resource use when measurable.
   Verify isolation from live endpoints and that the destination does not contain existing data.
   If isolation or authorization is unresolved, complete metadata review and report that the
   restore was not performed. Dismissed/declined/expired/interrupted restore approval changes nothing.
7. Restore only the selected backup using the existing mechanism. Check integrity, expected schema/
   data and representative application reads in isolation; avoid sending restored events/messages
   to live consumers. Record actual duration and recovered point. Never claim contents verified
   from backup metadata alone.
8. Clean up only task-owned test resources when their removal was authorized in the plan.
   Preserve backup copies and any evidence needed to assess recoverability.
9. Report backup/source/time, integrity checks, destination, actual restore/validation outcome,
   measured recovery timing, cleanup and any missing keys, access or unverified recovery steps.
