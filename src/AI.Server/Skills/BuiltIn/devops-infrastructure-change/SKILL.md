---
id: devops-infrastructure-change
name: Devops infrastructure change
icon: devops-infrastructure-change
kind: playbook
description: Prepare and validate an infrastructure-as-code change with the project's existing tooling, show its exact plan and apply only specifically authorized live changes.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The exact infrastructure environment/workspace"},"goal":{"type":"string","description":"The infrastructure change to make"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
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

4. Identify the existing IaC tool/version, modules, provider pins, workspace/account, backend and
   state-locking conventions. Inspect current configuration and permitted state metadata without
   exposing sensitive values. Never switch backend/workspace, import resources or unlock state
   automatically. Report missing or conflicting context before mutation.
5. Implement the authorized configuration change while preserving unrelated resources. Validate
   and format only affected files with the configured tools. Handle provider/module version
   changes separately and maintain lockfiles; do not upgrade everything to resolve one issue.
6. Generate the tool's actual plan/change set with explicit context and existing state locking.
   Inspect creates, updates, replacements, deletions, security/network exposure and cost-related
   resource changes; redact sensitive plan values. A speculative source diff is not an IaC plan.
   Prepare recovery/export steps appropriate to the tool; never imply state alone backs up data.
7. Show the concrete plan. Apply only that reviewed plan/change set when explicitly authorized.
   Verify the artifact/diff/context has not changed; regenerate and reassess after drift, source
   edits, expiry or concurrent changes. Never blindly use auto-approve, force unlock or disable
   locking. Keep state/plan artifacts with the repository's secret-handling conventions.
8. Inspect provider results, remaining drift and relevant service checks. Report partial changes
   precisely; do not destroy/recreate resources or restore state blindly to recover an error.
9. Report changed configuration, plan summary, whether it was applied, target context, actual
   checks, retained plan/state references and any remaining recovery work.
