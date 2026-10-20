---
name: agent-eval
description: Run the AI.Client agent evaluation (evals/AI.Agent.Evals), analyze failed and expensive attempts, propose or make a harness change, and re-run to compare. Use when asked to evaluate, benchmark or improve the harness, to analyze an eval run, or to compare two runs.
---

# Agent evaluation loop

The runner and scenario format are described in `docs/13-headless-chat-testing.md`. Read it first if
anything below is unclear. The endpoint comes from `evals/connection.json` (git-ignored); never print
or commit its key.

## 1. Run

```bash
dotnet run --project evals/AI.Agent.Evals -- list
dotnet run --project evals/AI.Agent.Evals -- run --repeat 3 --parallel 3 [--filter <id,tag:name>]
```

Runs take minutes; start them in the background and wait
for completion instead of polling. Each run prints its result directory under `evals/results/`.
Before changing anything, make sure a baseline exists for the current commit and model.

## 2. Analyze

Start from `summary.md` of the run. For every scenario that failed at least once, and for the most
expensive passing ones, read the attempt timelines:

```bash
dotnet run --project evals/AI.Agent.Evals -- trace evals/results/<run>/attempts/<scenario>/<n> --width 200
```

Go to `chat.json`, `events.jsonl`, `host.log` and `data/logs` only when the timeline is not enough.
For repository scenarios, `changed/` holds what the model edited; compare it with the hidden files
(the scenario's `hidden/` folder, or the repository at the hidden file's commit) to judge how close it came.

Classify every failure into exactly one cause:

| Cause | Signs |
| --- | --- |
| harness error | Host/tool exception, wrong tool result, run failed or interrupted, runner `error` outcome |
| tool selection | the needed tool was not offered (routing), or the model searched for a tool it lacked |
| tool use | wrong or invalid arguments, the same call repeated, a result misread |
| early stop | the turn ended with work announced but not done, or without verifying |
| lost context | after a compaction (`compactions` > 0) the model forgot a constraint or a fact |
| instruction | the model ignored the task's or the harness's instructions (changed forbidden files, did not ask) |
| capability | the model understood the task and the harness worked, but the solution is wrong |
| scenario | the check or the scenario itself is wrong or flaky |

Also note cost signals even in passing attempts: input tokens per step, cache hit rate, repeated calls,
needless `plan_update`/`run_skill` steps, `exit≠0` noise.

Write the analysis to `evals/results/<run>/analysis.md`: a table of attempts with cause and one line of
evidence each, then the causes ranked by how many attempts they cost, then at most three proposed
changes, each with the files to change and the scenarios expected to improve. Only `harness error`,
`tool selection`, `tool use`, `early stop`, `lost context` and `instruction` are the harness's to fix;
`capability` belongs to the model and `scenario` to the scenario.

## 3. Change and compare

Make one change at a time (instructions, tool descriptions, routing, compaction or code), following
`AGENTS.md`. Run the scenarios it targets with the same `--repeat`, then the whole set, and compare:

```bash
dotnet run --project evals/AI.Agent.Evals -- compare evals/results/<baseline> evals/results/<candidate>
```

Accept a change only if no scenario's pass rate drops and the targeted metric improves beyond the
spread between repeats. Report the comparison table, and say plainly when the difference is within
noise. Do not commit or push unless asked.
