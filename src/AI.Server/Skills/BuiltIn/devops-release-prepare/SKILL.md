---
id: devops-release-prepare
name: DevOps release prepare
icon: devops-release-prepare
kind: playbook
description: Prepare a selected release version, notes, packages and checksums using existing build commands and verify the artifacts; never publishes or deploys.
parameters: {"type":"object","properties":{"version":{"type":"string","description":"The exact release version"},"runtime":{"type":"string","description":"The requested runtime/platform"},"branch":{"type":"string","description":"The selected release source branch"},"commits":{"type":"array","items":{"type":"string"}},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Configuration files or directories explicitly named or selected"}},"additionalProperties":false}
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

4. Identify release conventions, version sources, supported platforms, previous release and existing
   packaging targets. Select the source revision/commits with Git pickers only if missing.
   Check the requested version for format and collisions. Do not invent a version or change Git
   history/tags. Report a dirty working tree and whether it affects the intended artifact;
   never silently package unrelated local changes as a named committed release.
5. Produce release notes from actual selected changes and documented breaking behavior. Use
   the repository's version/packaging workflow, updating only authorized version metadata.
   In this project, inspect documented publish/package-release targets before choosing commands;
   dotnet publish creates artifacts, not an external deployment.
6. Build/test and package the selected platforms. Inspect archive contents and executable/Web
   assets, verify launch/smoke behavior where supported, compute checksums and retain the
   association between version, source revision, platform and artifact. Apply signatures only
   through already configured, authorized signing mechanisms; never request signing keys in chat.
7. Use the existing documented validators, build/test commands, dry runs and safe checks relevant
to the task. For test commands, filter to the affected project/module/tests first and use a full
suite only as a final relevant check. Select supported quiet/minimal runner output that preserves
failure details and counts. Set an explicit finite test timeout within the tool limit; split long
suites and report timeouts as incomplete. Read results and exit codes; distinguish success, failure and unavailable
checks. Never disable security checks, weaken tests or claim a validation that did not run.
Compare final repository status with the baseline and remove only scratch artifacts created by
this task. Never discard unrelated local or remote work.

8. Report version/source, release notes, artifact paths/checksums and actual checks. List missing
   platform/signing verification. Do not create remote releases, tags, uploads or deployments.
