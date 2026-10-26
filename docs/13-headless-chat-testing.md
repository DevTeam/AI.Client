# Headless chat testing and agent evaluation

`evals/AI.Agent.Evals` runs real tasks through the real harness and a real model, without a UI, and
measures how they went. It is the loop for improving the harness: run the scenarios, read what failed,
change instructions, tools or code, and run them again against the same baseline. Like the other
model evaluations it is outside `AI.slnx` and CI, and every run costs model tokens.

The earlier `AI.Cli` session tool is gone; this runner drives the same server-side chat, Submit and
run API a client uses.

## Running

The endpoint is read from `evals/connection.json`, which git ignores; copy
`evals/connection.example.json` and fill in `baseUrl`, `model`, `apiKey`, and optionally `contextWindow`,
`reasoningEffort` and `prices`. `AI_AGENT_EVAL_BASE_URL`, `_MODEL`, `_API_KEY`, `_CONTEXT_WINDOW`,
`_REASONING_EFFORT` and `_PRICES` override single fields; `--connection <file>` reads another file.

```powershell
dotnet run --project evals/AI.Agent.Evals -- run --repeat 3
dotnet run --project evals/AI.Agent.Evals -- compare evals/results/<baseline> evals/results/<candidate>
```

| Option | Meaning |
| --- | --- |
| `--filter <id,tag:name>` | Scenarios whose id contains a part, or which carry the tag |
| `--include-slow` | Without `--filter`, also run scenarios tagged `slow` |
| `--repeat <n>` | Attempts per scenario; use at least 3 before trusting a difference |
| `--parallel <n>` | Attempts at once; each has its own Host, so they do not interfere |
| `--configuration`, `--no-build` | The Host is built from the working tree first unless `--no-build` |
| `--no-route` | Switch skill routing off, to measure what it contributes |
| `--scenarios`, `--out` | Other scenario or result directories |

A small `contextWindow` forces compaction; `prices` (`{ "input": 1, "output": 2, "cachedInput": 0.1 }`
per million tokens, or `input,output[,cachedInput]` in the variable) prices endpoints that do not price
their usage. The key is passed to the Host through its API and never written to a report; the copied
data directory leaves out `credentials`.

`list` prints the scenarios. The exit code of `run` is 0 only when every attempt passed.

### CLI setup evaluations (Windows)

Run `--filter tag:cli-setup --repeat 3 --parallel 2` to exercise real .NET, gh and npm commands,
directory selection and batch grants, dismissal, unauthenticated gh, a failing .NET self-check and
an exit-125 diagnostic that distinguishes a metadata warning from a fatal launcher error.
These scenarios require the CLIs on the execution host; npm currently targets the standard
`C:\Program Files\nodejs` installation. Their `platform: "windows"` excludes them on other OSes;
scenarios can likewise specify `linux` or `macos`. They are not cross-platform compatibility claims.

A scenario's optional `external/` fixture is copied beside, not inside, the granted workspace.
`{external}` in turns and prompt answers selects that private per-attempt copy. No user's real
credentials or configuration are copied. Prompt rules support `paths` for a real multi-directory
picker answer and `dismiss` for refusal; an unanswered picker does not synthesize authorization.
The runner saves `project.json` alongside `chat.json` so checks see actual persisted grants.
`projectGrantCount` and `projectGrantMatches` inspect them. `toolTranscriptMatches` checks calls
and actual result messages in order; `maxMatchingCalls` limits grant batches. Checks must establish
success from sandboxed tool results, since post-run command checks execute outside the sandbox.

`audit-cli <run-dir>` rechecks saved CLI attempts with the current scenario assertions, pairing
each tool result with its own call ID. It writes `audit-cli.json` and `audit-cli.md` without changing
the original result or summary files or making model requests. This allows checker corrections
to be verified against the original evidence; it does not substitute for a new model run after
changing a skill. Calls-not-match patterns should anchor the tool name, since plan arguments can
mention tools that were never called. A later successful version probe must not validate an earlier
failed original command.

Deterministic runner checks live in `evals/AI.Agent.Evals.Tests`, outside normal CI. They verify
picker answers, dismissal and that grant/transcript verdicts require real evidence.

## One attempt

1. The scenario's `workspace` folder, or the files of a repository commit, are copied to a temporary
   directory, made a git repository with one commit (unless `"git": false`), and hashed.
2. A Host starts from the built `AI.Host.dll` with its own `--data-dir` and a free loopback port, so no
   settings, memory, policies or usage leak from the person's installation or between attempts.
3. The evaluated connection becomes the only and default one. Auto titles, reply and comment
   suggestions are off, because they cost requests nobody asked for; skill routing stays on.
4. A project grants the workspace copy with `read`, `write`, `edit` and `delete`; the chat uses
   `FullAccess`, so tool policies still apply but safe calls need no card.
5. Each turn is submitted, and `/api/runs` is polled. A pending approval is allowed unless the scenario
   denies the tool; an `ask_user` prompt is answered by the scenario's rules, else by the recommended
   option, else dismissed (the model decides). A turn ends when its run completes, fails or is interrupted.
6. After the last turn, or the timeout (the run is then stopped), the chat, its usage and the Host's data
   are saved, the hidden files are written into the workspace, and the checks run against the workspace
   and the final answer.

## Scenarios

A scenario is a directory in `evals/scenarios` with `scenario.json` and, optionally, a `workspace`
folder. Comments are allowed in the JSON.

```jsonc
{
  "id": "fix-failing-self-check",
  "description": "What it tests, one line.",
  "tags": ["edit", "dotnet"],
  "turns": ["The self-checks in {workspace} fail. Fix the bug."],   // {workspace} is the copy's path
  "prompts": [{ "match": "which test", "option": "unit" }],          // ask_user answers; or "answer", "dismiss"
  "denyTools": ["fetch"],                                            // approvals refused
  "timeoutSeconds": 900,
  "checks": [
    { "type": "command", "executable": "dotnet", "arguments": ["run", "--project", "Stats"], "exitCode": 0 },
    { "type": "onlyChanged", "paths": ["Stats/Statistics.cs"] }
  ]
}
```

| Check | Passes when |
| --- | --- |
| `command` | a program run in the workspace (or `path` under it), given `input` on its standard input, exits with `exitCode` within `timeoutSeconds`, and its output matches `outputPattern` when one is given |
| `fileExists`, `fileAbsent` | the file at `path` does or does not exist |
| `fileContains`, `fileNotContains` | the file's text does or does not match `pattern` |
| `fileUnchanged` | the file is byte-for-byte the fixture's |
| `noWorkspaceChanges` | nothing was added, changed or deleted |
| `onlyChanged` | every change lies under `paths` |
| `answerMatches`, `answerNotMatches` | the final answer does or does not match `pattern` |
| `workspaceContains`, `workspaceNotContains` | some text file (under `paths`, when given) does or does not match `pattern` |
| `maxToolCalls`, `maxToolErrors` | the attempt stayed within `value` |
| `minPrompts`, `maxPrompts` | the model asked the person (`ask_user`) at least or at most `value` times |
| `callsMatch`, `callsNotMatch` | some or no tool call, as `name arguments`, matches `pattern` — for instructions such as "do not run the tests" |

Checks too specific for these types run a script: `evals/scenarios/.shared/check_translation.py` compares a
Markdown translation with its source (headings, untouched code blocks and links, the share of prose in
the target language, nothing left out). `{scenario}` in a command's arguments is the scenario's own
directory, for files the model must not see:
`write-tests-for-parser` copies a mutant of the parser from there and expects the model's tests to fail
on it, so tests that merely pass are not enough. Patterns are .NET regular expressions matched case-insensitively. Build output (`bin`, `obj`) is not part
of the workspace comparison. Prefer checks a program decides over wording: a test that passes says more
than an answer that mentions the fix.

`contextWindow` sets the window the connection declares for one scenario, so a small one exercises
compaction regardless of the model.

### Real repositories

A scenario can work on a commit of a real repository instead of a fixture, the way SWE-bench does:

```jsonc
{
  "repository": { "path": "../Pure.DI.Solution", "commit": "<commit>" },
  "hiddenFiles": [{ "path": "Lib.Tests/Integration/ClockGreetingTests.cs" }],
  "turns": ["In the solution at {workspace}, change the greeting to ..."],
  "checks": [{ "type": "command", "executable": "dotnet", "arguments": ["test", "Lib.Tests"] }]
}
```

`path` is absolute or relative to this repository's root; the `solution-*` scenarios expect a
Pure.DI.Solution checkout next to it and the `csi-*` ones a csharp-interactive checkout, whose checks
build the solution but never run its slow tests. The commit's files are exported with `git archive`, without
history, so a later fix cannot be read from the log. A `workspace` folder beside such a scenario is
laid over the exported files, to plant a bug. Hidden files are written only after the model has
finished: with `commit` they come from that commit of the repository (the tests a real fix added),
without it from the scenario's own `hidden/` folder. Check that hidden tests fail before the change
and pass with a correct one before adding a scenario.

Prefer repositories that build and test in seconds. Scenarios tagged `slow` are skipped by `run`
without `--filter` unless `--include-slow` is given; a large repository whose test suite models run
in full made every attempt take most of its 40 minutes and millions of tokens, and its scenarios
were dropped.

Good sources of scenarios are real tasks from one's own chats and fix commits: the bug a commit fixed,
stated as the person met it, becomes a regression scenario. Keep each fixture small and self-contained,
with no package restore from the network when possible.

## Results

A run writes `evals/results/<time>-<commit>-<model>/` (ignored by git):

- `summary.md` and `summary.json` — per scenario: passes, outcomes, means of tool calls, failed calls,
  programs that exited non-zero (counted apart from failed calls: a failing test run is a result, not a
  tool failure), repeated calls (same tool and arguments as before in the attempt), compactions, model
  steps, tokens, cache hit rate, cost and time, and which checks failed how often;
- `attempts/<scenario>/<n>/result.json` — the attempt's metrics, requests by purpose (answer, routing,
  summaries), workspace changes, check details and final answer;
- `events.jsonl` — turns, run state changes, active tools, approvals and prompts with their answers,
  context usage, with milliseconds since the attempt started;
- `chat.json`, `usage.json`, `host.log`, `changed/` (the changed files) and `data/` (the Host's logs,
  chat files and usage ledger).

`trace <attempt-dir>` prints one attempt as a short timeline: the checks, each user message, each
step's text and calls, and the head of each result marked `ok`, `ERROR` or `exit≠0`.

`compare` prints, scenario by scenario, the pass rate and the mean tool calls, failed calls, tokens,
cost and time of two runs. Results depend on the model and on chance: compare runs of the same model
and endpoint, and repeat a scenario before trusting a single flip.

## Improving the harness with it

The project skill `.claude/skills/agent-eval` walks Claude Code through this loop: run, trace the failed
and expensive attempts, classify each failure by cause, write `analysis.md` next to the run, change one
thing and compare.

1. Run the scenarios on the current commit to get a baseline.
2. Read the failed attempts: `result.json` for which check failed, `events.jsonl` and `chat.json` for
   why. Group failures by cause (wrong tool, bad arguments, early stop, lost context, too many
   questions, instruction ignored, harness error) before changing anything.
3. Change one thing — an instruction, a tool description, a routing rule or code.
4. Run the affected scenarios, then all of them, and `compare` with the baseline. Keep some scenarios
   out of the tuning, and accept a change only when they do not get worse.
