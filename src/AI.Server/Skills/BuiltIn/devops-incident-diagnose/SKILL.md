---
id: devops-incident-diagnose
name: DevOps incident diagnose
icon: devops-incident-diagnose
kind: playbook
description: Investigate an operational incident using logs, metrics, configuration and recent changes, report supported causes and a concrete recovery plan; changes nothing.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The affected environment"},"service":{"type":"string","description":"The affected service"},"symptom":{"type":"string","description":"The symptom, alert or observed failure"},"since":{"type":"string","description":"The incident time window"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","process_run","trigger_wait","ask_user"]
---

For large diagnostic logs or traces, use the `purpose: "chatTemporary"` root from
`list_allowed_directories` for task-only files when available. Redact sensitive data,
inspect relevant excerpts, preserve command exit codes, and keep deliverables outside
scratch storage. Remove task-created scratch files when finished.

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

Keep this skill read-only: do not edit configuration, restart services, change infrastructure,
rotate secrets, trigger jobs, restore over data or deploy. Never stop, kill or restart a process
this run did not start — an application, a service, another chat's job. Freeing a stuck process is
part of the proposed recovery plan, not something this diagnostic does; if the user asks for it,
name the exact process or PID in `ask_user` and act only on an explicit affirmative answer, while
a declined, dismissed, expired or interrupted answer leaves it running. Use queries with no live side effects.
If a diagnostic would mutate state, report that limitation and the proposed next action instead.

4. Establish impact, time window and affected endpoints/users from supplied evidence. Correlate
   timestamps/time zones and deployment/configuration history. Query bounded relevant logs,
   metrics/traces and status through existing tools, redacting secrets and personal data. For a
   known local PID or read-granted log file already under observation, use one bounded
   `trigger_wait` call when offered for exit, metric threshold or change instead of repeated
   polling. It ends with this chat run; inspect the actual log or metric after it fires. Use provider-native
   status tools for remote services.
5. Follow request paths and dependencies. Distinguish application errors, resource exhaustion,
   networking/DNS/TLS, credentials, data stores, capacity and provider incidents. Compare with a
   healthy instance or baseline where available. Test one hypothesis at a time with read-only
   evidence; do not install agents, restart services, clear caches or increase limits here.
6. Identify the supported causal chain, not just a correlated event. When evidence is insufficient,
   state hypotheses, observations that support/refute them and the next discriminating query.
   Do not claim a definitive cause from a single log message.
7. Prepare a prioritized recovery plan with exact target/actions, expected effect, verification
   and rollback. Identify actions that need authorization; do not execute recovery under this
   diagnostic skill. An explicit request to deploy/rollback/fix then uses the matching skill.
8. Report impact, timeline, evidence/source references, cause or ranked hypotheses, completed
   queries, validation limits and the complete recovery plan.
