# Process sandbox

Status: Accepted

`process_run` is the default runner for commands, builds, tests and installed CLI tools. It runs
with the Host user's permissions, without an OS sandbox. The working directory and project
directory grants do not confine it. Its default policy is `Ask`; saved policies and command rules
apply, including in subtasks, which cannot ask for approval.

`process_run_sandboxed` takes the same arguments and is used when sandboxed execution is explicitly
requested. It and `cs_run` are confined to session grants, the chat's temporary directory and
read-only system locations. Both default to `Allow`. There is no switch that changes either
process tool's mode. The two process tools share command rules (`CommandRule.RulesOf`), resource
limits, managed handles, `process_wait`, `process_stop` and process-completion triggers.

## When an explicitly sandboxed run is denied

The sandbox opens no toolchain profile or package cache on its own. For explicitly requested
sandboxed execution, `code-cli-configure` can identify missing installation, configuration and
cache directories, collect approval and add the approved directory grants. Retry only after tool
sessions refresh their grants. If the user requires confinement and the operation is incompatible
with it, report that limitation. Ordinary CLI work starts with `process_run`; it does not first
attempt sandboxed execution or request sandbox directory grants.

## What a confined program may do

| Location | Access |
| --- | --- |
| Recursive grants with `write`, `edit` or `delete` | read and write |
| Recursive grants with `read` only | read |
| The chat's temporary directory (`chatTemporary` grant) | read and write; it is the program's `TEMP`/`TMP`/`TMPDIR` |
| The program's own directory, the .NET installation | read |
| OS libraries and configuration (`/usr`, `/lib*`, `/etc`, `/proc`, `/sys`, `/run`, `/opt` on Linux; `/usr`, `/System`, `/Library`, `/private/etc`, `/private/var/db`, `/opt` on macOS; `Windows` and `Program Files` on Windows) | read |
| `/dev` (Linux, macOS) | read and write |
| Anything else, the user profile with its package caches and credentials included | denied |

A non-recursive grant is left out of the policy: the OS mechanisms open whole trees, and opening the
tree would give more than the grant does.

Commands keep the network, because restoring packages, fetching from git and calling HTTP services
are ordinary work for them. Scripts run without it wherever the OS can close it
(`SandboxSupport.ClosesNetwork`: Windows, macOS, and Linux with Landlock ABI 4): a sandboxed script
runs unasked, and one that could read the grants and send them anywhere would be a way out of the
boundary. On an older Linux kernel the network stays open and the tool description says so.

Programs that keep caches or configuration in the user profile, such as NuGet, npm, pip and git,
fail under the sandbox unless those directories are granted; that is the case the grant flow above
is for. A program also cannot start in a working directory outside the grants.

## Design

`AI.Sandbox` is a project and assembly of its own. `IProcessLauncher.Start(ProcessLaunch)` starts a
program with everything that bounds it:

- the `SandboxPolicy`, the paths and their access, or none;
- the `ChildProcessLimits` of the whole tree, applied by `ChildProcessJob` (a Job Object on Windows);
- the deadline, after which the tree is killed.

It returns an `ILaunchedProcess`: the program's standard streams, its id and exit code, a `Deadline`
token for reads, `TimedOut`, `LimitExceeded`, `WaitForExitAsync` (which kills the tree at the deadline
or on cancellation) and `Dispose` (which kills whatever is left).

On Windows every program, confined or not, is created by `WindowsProcess` rather than
`Process.Start`: `CreateProcess` suspended, with the job in its attribute list and three pipes the
launcher made for its standard streams. The program is in its job before any of its code runs, so a
child it starts at once is counted and killed with it; with `Process.Start` the job could only be
assigned after the program was already running. `ProcessRunner` (`process_run`) and
`ScriptWorkers` (`cs_run`) only describe what to start; neither handles jobs, deadlines or the
sandbox itself.

The program is resolved to a full path before anything is confined: a bare name through the
system directories (Windows) and the `PATH` the program will see, with the `PATHEXT` extensions on
Windows. A missing program fails the start with the same `Win32Exception` as without a sandbox.

The policy is completed with the read-only system locations above and normalized: paths made
absolute, duplicates merged to the wider access.

### Windows: AppContainer

`ProcessLauncher` starts the `AI.Sandbox` executable (the launcher) with the policy as JSON and the
program after it. The launcher:

1. creates the `AI.Client.Sandbox` AppContainer profile once, or derives its SID;
2. derives a capability SID for each path and access from `AI.Client.Sandbox.<access>.<hash of the path>`;
3. adds an inheritable access entry for that capability to the path's ACL if it is not there yet;
4. starts the program in the container with exactly those capabilities, plus the network
   capabilities when the network is allowed, with the launcher's standard handles and already
   inside a kill-on-close job of its own (the launcher itself is in the caller's job);
5. waits for it and exits with its exit code.

Granting to capabilities rather than to the container lets one container serve every policy while a
launch reaches only its own policy's paths. The access entries stay: Windows copies an inheritable
entry to everything below the path the first time, which can take a while for a large tree, and
later launches start at once. They grant nothing to anyone without the capability. Read access to
the `Windows` and `Program Files` directories comes from `ALL APPLICATION PACKAGES` and needs no
entry. An AppContainer gets its own temporary directory (`%LOCALAPPDATA%\Packages\ai.client.sandbox\AC\Temp`)
whatever `TEMP` says, and cannot send ICMP or reach loopback services of other processes.

Resolving a granted path can also inspect its ancestors: Node, for example, checks each directory
up to the drive root when loading a script. The launcher gives those ancestors separate metadata
capabilities, derived from each ancestor's path. Their access entries allow traversal, attributes,
extended attributes and synchronization on that directory only, without inheritance. They do not
allow listing the ancestor, reading its files or modifying it, and another launch gets only the
metadata capabilities needed by its own policy. This also applies to ancestors of individual file
grants and the read-only program/runtime locations. An ancestor whose ACL cannot be updated stays
closed unless existing permissions already allow the operation; the launcher reports the failure.
In particular, a user may be unable to update a drive root's ACL. A CLI that explicitly queries that
root can still fail, even though other ancestor queries and access to the granted files work.
The launcher does not elevate privileges or replace a metadata grant with a recursive read grant.

Metadata entries are written with `SetSecurityInfo` through a `MAXIMUM_ALLOWED` directory handle,
which suppresses propagation to children. Updating them with `DirectoryInfo.SetAccessControl`
would otherwise reprocess inherited permissions throughout a potentially large ancestor tree,
even though the new entry itself does not inherit. Content grants retain their recursive behavior.

### Linux: Landlock

The launcher creates a Landlock ruleset that handles every file-system right the kernel's ABI
knows, adds a rule for each path, sets `no_new_privs`, restricts its own thread and replaces itself
with the program through `execve`. The restriction is inherited by everything the program starts
and cannot be lifted. Restricting and executing happen on the same thread, because
`landlock_restrict_self` applies to the calling thread only. Landlock needs Linux 5.13 with the LSM
enabled; `SandboxSupport` reports the ABI.

### macOS: Seatbelt

`ProcessLauncher` starts `/usr/bin/sandbox-exec` with a generated SBPL profile: everything is
allowed, file reads and writes are denied, metadata reads are allowed so path lookups work, and the
policy's paths are opened again (later rules win). Paths are resolved with `realpath`, because the
sandbox matches the real path (`/private/tmp`, not `/tmp`). `sandbox-exec` is deprecated but present
on every macOS and used by Chrome, Bazel and Nix.

### Failure modes

A policy the machine cannot enforce fails the start (`SandboxUnavailableException`) instead of
running the program unconfined. When the machine has no sandbox, `process_run` and `cs_run` stay
listed, their descriptions say so and every run fails with the reason; `process_run_sandboxed`
still works. A launcher failure before the program
runs exits with 125 and a message starting with `ai-sandbox:`.

Windows resets the security of the null device (`\Device\Null`, `NUL`) at every boot. On Windows 11
the reset opens it to AppContainers; on Windows 10 it does not, so a confined program cannot open
`NUL`, and no path in the policy changes that. Git for Windows opens `/dev/null` for reading and
writing at every start and fails with `could not open '/dev/null' for reading and writing:
Permission denied`. `ProcessLauncher` probes this once per process with `cmd /c echo.>nul` in a
container with nothing granted (`SandboxSupport.OpensNullDevice`). When the device is closed, the
descriptions of `process_run` and `cs_run` say so, and a failed `process_run` whose error output names
the device carries an `error` saying that a grant will not help and that
`process_run_sandboxed` will, so the model does not ask the user for a directory that cannot fix it.
Opening the device to containers would mean changing its security descriptor as an administrator
after every boot, which the application does not do.

## cs_run inside the sandbox

The worker is the server executable started with `--script-worker`. Its policy is the session's
plus read access to the server's directory, where the compiler is. It starts in that directory and
sets the script's working directory itself. `DOTNET_EnableDiagnostics=0` keeps the runtime from
opening its diagnostic channel in the user's temporary directory. The spare worker that
`ScriptWorkers` keeps warm is started in the same sandbox and job.

## Tests

- `tests/AI.Sandbox.Tests` starts shell commands through `ProcessLauncher` on the OS that runs the
  tests: reading and writing inside and outside the policy, children of a confined program, exit
  code, working directory, environment, standard input, the deadline, cancellation, a missing
  program and the Windows process limit.
- `tests/AI.Mcp.BuiltIn.Tests/SandboxedToolTests` starts the built-in server as the Host does and
  checks `process_run_sandboxed` and `cs_run` against read-only, writable, temporary and
  ungranted directories, and verifies that ordinary `process_run` can use host access outside grants.

A machine without a sandbox skips the confined tests. CI sets `AI_SANDBOX_REQUIRED=1`, which turns
that skip into a failure, and runs every test on Windows, Linux and macOS in parallel
(`.github/workflows/ci.yml`).
