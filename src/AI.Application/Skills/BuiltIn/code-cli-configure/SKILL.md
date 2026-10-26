---
id: code-cli-configure
name: Code CLI configure
icon: code-run-shell
aliases: ["cli-setup","cli-configure"]
kind: playbook
description: Configure an existing CLI for process_run, including dotnet/.NET, gh, Git, npm/Node and pip/Python, or diagnose inaccessible installation, configuration and cache directories. Connect the user's existing directories through visible project grants; distinguish sandbox failures from authentication, missing programs and failing tests.
parameters: {"type":"object","properties":{"cli":{"type":"string","description":"CLI to configure"},"command":{"type":"string","description":"Original command or task to verify"},"error":{"type":"string","description":"Observed failure, when available"}},"additionalProperties":false}
tools: ["tool_search","app_read","app_security","ask_user","list_allowed_directories","process_run","process_run_unrestricted","get_file_info","list_directory","read_text_file","fetch"]
---

Set up the requested CLI using the current project's ordinary directory grants. Prefer the user's
existing installation, configuration and caches. Do not create a separate environment, replace
user configuration or install tools unless the task requires it and the user authorizes it.

1. Read the project and revision with `app_read` resource=Project and inspect
   `list_allowed_directories`. Identify the execution host's OS, executable and working directory
   from available execution capabilities and permitted checks. Resolve relevant child programs too:
   a shell's installation is not the installation of every CLI it starts. Do not assume a client
   path, a PATH entry or a successful `--version` proves the whole toolchain works.
2. Inspect the original result: `exitCode`, `error`, `stdout`, `stderr`, `timedOut`, `truncated`.
   A PATH-update warning can accompany a successful .NET command. A missing executable, expired
   login, HTTP 401/403, failing tests, resource limit or unsupported sandbox operation needs its
   own diagnosis; none alone is evidence that another directory grant will help. Obtain a bounded
   diagnostic or complete authorized log when the failure is truncated. Do not dump credentials.
3. Determine the narrowest required directories and access from the actual installation, error
   paths and the CLI's current documentation. Discover and use a fetch tool only when documentation
   is needed. Respect current grants while investigating; do not use an unrestricted process to
   inspect denied directories without its normal authorization. Use these as leads, not a fixed
   list of paths to grant:

   | CLI | Installation and configuration | Writable state |
   | --- | --- | --- |
   | .NET/NuGet | Actual SDK, project and user NuGet.Config, configured package/credential providers | Actual global packages, HTTP cache and CLI home |
   | Git | Git installation and helpers, global config, configuration includes | Only the repositories/state the task changes |
   | gh | gh installation, GH_CONFIG_DIR or the platform's existing gh configuration | Config/state only when the requested command changes it |
   | Node/npm | Node, npm entrypoint and modules, project/user npm configuration | Configured npm cache and install destinations |
   | Python/pip | Actual interpreter or virtual environment, pip configuration | Configured pip cache and requested environment |
   | Other | Discover executable, dependencies, configuration includes and supported path overrides | Identify the actual cache/state rather than guessing |

   Read/execute is sufficient for installations and read-only configuration. Cache maintenance
   commonly needs write access. Login stores, SSH agents and package-provider credentials are
   separate dependencies: a folder grant does not establish authentication. Do not read/copy
   secret files or grant an entire profile, drive, .ssh or credential directory merely for convenience.
   CLI-specific environment overrides are optional, not a reason to replace working user paths.
   The runner filters inherited environment; never claim an override persists between calls.
4. Collect all missing grants before asking. Check both recursive coverage and capabilities;
   read-only access does not cover writes. Explain each proposed path, its purpose and access,
   including that ordinary project grants also apply to file tools. Unless those exact paths and
   rights are already authorized, use the existing `ask_user` directory picker
   (`pathKind: "directories"`) and access choices in one call. Group paths with the same access;
   distinguish read-only configuration from writable caches. A dismissed, declined, expired or
   interrupted answer is not approval. If nothing is missing, do not request additional grants.
5. Reread the project, exclude covered paths, and call `app_security` operation AddDirectoryGrants
   once with `projectId`, the current `revision`, a fresh `operationId` and `directoryGrants`.
   Each grant needs a fresh `id`, meaningful `displayName`, absolute `canonicalRoot`, `recursive:
   true`, and `toolNames` ["read"] or ["read","write","edit","delete"] as approved. The batch
   adds ordinary entries visible in project folder settings without replacing MCP bindings,
   policies or existing folders. On conflict, reread and recompute the approved additions. Reconfirm
   only if their meaning or access changes. Never use SetProjectSecurity to append folders.
6. In the next model step, rerun the original command through `process_run`, respecting its side
   effects and limits. Newly opened sessions receive the grants; calls already submitted in the
   same batch retain the old ones. Verify the requested behavior, not merely the CLI version.
   Report the added paths/access and result. If still blocked, identify the remaining dependency
   rather than repeatedly broadening access. Windows ancestor metadata can remain inaccessible
   when the host user cannot change its ACL; directory grants also cannot fix unavailable devices,
   credential stores or other OS restrictions. Use process_run_unrestricted only under its existing
   approval rules when the user declines the needed grants or no grant can cover the operation.
