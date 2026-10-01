---
id: devops-config-review
name: Devops config review
icon: devops-config-review
kind: playbook
description: Review deployment and environment configuration for missing settings, conflicting values, secret exposure and unsafe defaults, reporting findings; changes nothing.
parameters: {"type":"object","properties":{"environment":{"type":"string","description":"The environment to inspect"},"service":{"type":"string","description":"The service to inspect"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user"]
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

Keep this skill read-only: do not edit configuration, restart services, change infrastructure,
rotate secrets, trigger jobs, restore over data or deploy. Use queries with no live side effects.
If a diagnostic would mutate state, report that limitation and the proposed next action instead.

4. Map how configuration is resolved: defaults, files, environment variables, deployment manifests,
   secret references and runtime overrides. Check precedence and required settings against the
   application's actual contracts, without printing secret values.
5. Compare requested environments using keys and behavior, not secret contents. Trace URLs, ports,
   service discovery, timeouts, retries, resource limits, probes, mounts, TLS/auth settings and
   debug flags. Distinguish deliberate differences from contradictory or missing values.
6. Check whether secrets or environment-specific data can enter artifacts, logs or source control,
   and whether production values accidentally point to test services or local paths. Validate
   configuration schemas safely; querying secret metadata does not require reading its value.
7. Report supported findings with severity, environment/service, source location, effective
   behavior and focused correction, plus validation limits. Do not edit files or remote settings;
   infrastructure configuration review is distinct from application code security review.
