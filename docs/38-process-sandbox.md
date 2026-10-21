# Process sandbox

Status: Accepted

`process_run` and `cs_run` start programs. Without a sandbox those programs run with everything the
Host user can do, and the project's directory grants bind only the file tools. The sandbox closes
that gap: when it is switched on, a started program and everything it starts reach only the
session's grants. It is off by default and switched on in the global settings ("Run commands and C#
scripts in a sandbox"). The Host then starts the built-in server with `AI_CLIENT_SANDBOX=1`.

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
tree would give more than the grant does. The network stays open, because restoring packages,
fetching from git and calling HTTP services are ordinary work for these tools. The policy type can
close it (`SandboxPolicy.AllowNetwork`). On Linux this needs Landlock ABI 4.

Programs that keep caches or configuration in the user profile, such as NuGet, npm, pip and git,
fail under the sandbox unless those directories are granted. This is why the sandbox is off by
default. A program also cannot start in a working directory outside the grants.

## Design

`AI.Sandbox` is a project and assembly of its own. `IProcessLauncher.Start(ProcessLaunch)` starts a
program with everything that bounds it:

- the `SandboxPolicy`, the paths and their access, or none;
- the `ChildProcessLimits` of the whole tree, applied by `ChildProcessJob` (a Job Object on Windows);
- the deadline, after which the tree is killed.

It returns an `ILaunchedProcess`: the ordinary `Process` with its redirected streams, a `Deadline`
token for reads, `TimedOut`, `LimitExceeded`, `WaitForExitAsync` (which kills the tree at the deadline
or on cancellation) and `Dispose` (which kills whatever is left). `ProcessRunner` (`process_run`) and
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
4. starts the program suspended in the container with exactly those capabilities, plus the network
   capabilities when the network is allowed, and with the launcher's standard handles;
5. puts the program in a kill-on-close job, resumes it, waits and exits with its exit code.

Granting to capabilities rather than to the container lets one container serve every policy while a
launch reaches only its own policy's paths. The access entries stay: Windows copies an inheritable
entry to everything below the path the first time, which can take a while for a large tree, and
later launches start at once. They grant nothing to anyone without the capability. Read access to
the `Windows` and `Program Files` directories comes from `ALL APPLICATION PACKAGES` and needs no
entry. An AppContainer gets its own temporary directory (`%LOCALAPPDATA%\Packages\ai.client.sandbox\AC\Temp`)
whatever `TEMP` says, and cannot send ICMP or reach loopback services of other processes.

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
running the program unconfined. When the sandbox is switched on but unavailable, the tool
descriptions say so and every run fails with the reason. A launcher failure before the program
runs exits with 125 and a message starting with `ai-sandbox:`.

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
- `tests/AI.Mcp.BuiltIn.Tests/SandboxedToolTests` starts the built-in server as the Host does with
  the sandbox on and checks `process_run` and `cs_run` against read-only, writable, temporary and
  ungranted directories.

A machine without a sandbox skips the confined tests. CI sets `AI_SANDBOX_REQUIRED=1`, which turns
that skip into a failure, and runs every test on Windows, Linux and macOS in parallel
(`.github/workflows/ci.yml`).
