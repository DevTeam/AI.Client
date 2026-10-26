# CLI sandbox compatibility evaluation

Run from the repository root on Windows:

```powershell
dotnet run --project evals/AI.Cli.Evals
```

An optional first argument sets the output directory. Use a new directory for each run so that
first-run sentinels and existing grants do not alter the experiment. The default writes to the
ignored `evals/results/cli-<UTC timestamp>-<id>` directory. Fixtures and complete structured results
are retained there. `results.json` includes stdout, stderr, commands, session grants, sandbox support
and baseline version attempts; `summary.md` contains the outcome matrix.

This deterministic evaluation calls the real `ProcessRunner`, `ProcessLauncher` and Windows
AppContainer. It does not use a model, the Host, or the agent evaluation connection. Agent scenario
post-run `command` checks execute outside the sandbox and cannot establish this compatibility.
No production policies are changed. Fixture permissions are applied by the existing launcher as
they are in a normal tool session. User profiles are never granted, and credentials are not copied.
Authentication and private package feeds are outside this evaluation.

Baseline cases are observations. Candidate cases require a successful exit (and known output for
configuration and content checks). Exit code 1 means a candidate still fails, not that the runner
failed to build. Missing executables are omitted from the tool-specific cases; baseline discovery
results remain in the JSON. Python, Go and Rust are not part of this matrix.

Batch fixtures avoid mixing `cmd /c` inline quoting with filesystem compatibility. Direct executable
cases separately test the launcher's program-directory handling. Do not equate `--version` success
with the ability to load a script, read configuration, restore packages or authenticate.

## Baseline findings on 2026-10-26

Measured on Windows AppContainer with .NET SDK 10.0.402, Git 2.45.1.windows.1, Node 24.20.0 and
PowerShell 7.6.5. These are machine-specific observations, not guarantees for other installations.

| Tool | Observation | Implication |
| --- | --- | --- |
| .NET | A fresh, writable CLI home prints the failed-PATH-update warning, but `dotnet new --no-restore` exits 0. | The warning alone does not explain a failed test command. |
| .NET | Redirecting CLI home and package cache and disabling PATH/certificate setup still leaves a build unable to read the user's NuGet.Config. An explicit restore config also fails in this probe. | The settings hierarchy needs its own treatment. |
| .NET | Additionally redirecting APPDATA and LOCALAPPDATA lets the minimal, package-free project build with 0 warnings and errors. | A consistent profile is more effective than isolated cache variables; private feeds and real integration tests still need separate evaluation. |
| gh | Even `gh --version` fails while trying to read the user's configuration. Reading a synthetic config through GH_CONFIG_DIR succeeds after a targeted read grant. | Configuration access is a startup dependency. This says nothing about credential-store access. |
| Git | `--version` succeeds, but reading a granted synthetic global config fails while statting a parent of the working directory, including with explicit `-C`. | Granting a config file alone does not resolve this installation's path handling. |
| Node | A direct `--version` succeeds, but executing a script inside the writable workspace fails with `EPERM: lstat 'D:\'`. | Ancestor path metadata access must be investigated independently of file content access. |
| npm | The wrapper is not found through cmd. Directly invoking its existing JS entrypoint through Node reports MODULE_NOT_FOUND. Cache redirection does not get far enough to be evaluated. | Resolve installation and path access before drawing conclusions about the cache. |
| PowerShell | The wrapper is not found through cmd; direct execution succeeds and reads the granted synthetic file. | Programs launched through a shell need dependency discovery and installation access too. |
| Boundary | A later cmd launch without the earlier file grant still cannot read the sentinel. | The targeted grant did not make that file readable in every subsequent launch. |

## Proposed design

1. Keep OS path compatibility in `AI.Sandbox`: investigate narrow ancestor traverse/attribute
   access, executable lookup and actual installation permissions. Do not recursively grant drive
   roots to fix a metadata lookup. The current Windows implementation assumes Windows and Program
   Files are already readable by AppContainers; validate that assumption for installed toolchains.
2. Prepare a coherent, persistent project execution profile: home, application data, configuration,
   caches and temporary files. Preserve project configuration and make importing user configuration
   explicit, especially for authenticated package sources.
3. Add small declarative CLI adapters for supported overrides and startup behavior, such as
   DOTNET_CLI_HOME, NUGET_PACKAGES, DOTNET_ADD_GLOBAL_TOOLS_TO_PATH, GH_CONFIG_DIR,
   GIT_CONFIG_GLOBAL and npm cache/userconfig. Apply the environment before a shell starts so child
   programs inherit it. Do not limit detection to the top-level executable name.
4. Handle credentials separately from non-secret configuration. A fresh profile does not reproduce
   the user's gh login, SSH agent or package-provider credentials.
5. Keep an explicit unrestricted fallback for genuinely incompatible CLI behavior. Never infer new
   permissions automatically from stderr or treat cached tool discovery as authorization.

## After ancestor metadata capabilities

With the Windows ancestor metadata change, the same matrix shows Git successfully reading the
synthetic global config, including with an explicit working directory. The earlier parent-stat
failure is gone. The denied config remains inaccessible without its content grant.

Node still fails on `lstat D:\` on this machine: the launcher reports that the current user cannot
update the drive root's ACL. Other protected ancestors can likewise reject metadata ACL updates.
No elevation or recursive ancestor read is used. The .NET configuration, npm entrypoint and shell
program-discovery cases retain their independent failures; they are outside the metadata change.

The matrix intentionally continues to report these unresolved compatibility failures.
