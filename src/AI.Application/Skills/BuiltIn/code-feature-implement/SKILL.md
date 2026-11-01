---
id: code-feature-implement
name: Code feature implement
icon: code
kind: playbook
description: Implement a feature or code change end to end: explore, plan, edit files, run the build and tests, remove leftovers and summarize the change; never commits.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"What to build or change, in the user's words"},"scope":{"type":"string","description":"Files, modules or limits the user named"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","write_file","edit_file","create_directory","move_file","delete_file","list_allowed_directories","process_run","process_wait","process_stop","ask_user","spawn_subtask","subtask_wait","subtask_stop","run_skill","app_read","tool_search"]
---

Before exploring multiple independent areas, read the full shared execution policy with
`app_read` resource=Skills, query=chat-execution-select, unless its body is already loaded;
a catalog description does not count. If app_read is not offered, discover it once with
`tool_search` before falling back to direct work. Apply the policy after brief research and
when dependencies change; keep this skill and its task active. For a small task in one area,
work directly without this lookup.

For verbose diagnostics or intermediate files, use the `purpose: "chatTemporary"` root
from `list_allowed_directories` when available. Preserve command exit codes, inspect only
relevant excerpts of saved output, and keep final artifacts in their intended location.
Remove task-created scratch files during cleanup.

The project instructions and instruction files (AGENTS.md, CLAUDE.md, CONTRIBUTING) win over these
steps where they differ: their build, test and style rules are the ones to follow.

1. Baseline. Find the repository root among the granted directories. When it has `.git`, run
   `process_run` with `git` and `["status","--porcelain=v1","-uall"]` in that directory, and
   `["rev-parse","--abbrev-ref","HEAD"]` for the branch. Paths already modified or untracked now
   are the user's work: never revert, reformat or delete them, and keep them out of the summary.
2. Explore. Locate the code with `grep_files` and `search_files` before reading; read the files
   the change touches, their callers, their tests and one similar existing feature whose patterns
   you will copy. Use the shared execution policy when a broad search can be split independently.
3. Clarify only what is genuinely the user's decision and costly to guess (behaviour, public API,
   data format): one `ask_user` call, the recommended option first. Take the conventional choice
   for everything else and name it in the summary. A dismissed question takes the recommended
   option; an expired or interrupted one stops here with the plan as the answer.
4. Plan. When the change spans more than three files, write a short numbered plan (file and what
   changes) in your text before the first edit. Stay inside `goal` and `scope`: no unrelated
   refactoring, renames, formatting or dependency upgrades, and ask before adding a dependency.
   Apply the shared execution policy to independent parts; start useful subtasks or team-assemble
   without asking the person to choose the execution method. In a teammate branch, keep the
   charter's ownership and reporting protocol while doing the assigned work.
5. Implement with `edit_file` for changes and `write_file` for new files. Match the surrounding
   code's naming, idiom and comment density. Add or update tests for the new behaviour where the
   project has tests.
6. Verify. Take the build and test commands from the instructions, README, CI files or manifests
   (package.json scripts, *.sln or *.csproj, pyproject.toml, Cargo.toml, go.mod, Makefile). Build,
   run the tests nearest to the change with a project/module/class/test filter, then the wider or
   full suite as a final check when relevant and feasible within the timeout. Use the runner's
   supported quiet/minimal output or concise reporter and short traceback options while keeping
   failure details and counts available. Set an explicit finite timeout for each test run within
   the effective tool limit; continue a running process with `process_wait` and report a hard timeout as incomplete.
   Read the output and exit code; on a failure fix the cause and run again, at most three rounds
   per failure. Never skip, weaken or delete a test to get green. Claim only what you ran: "should
   work" is not a result. When something cannot be run, say what and why.
7. Clean up. Run the status command again and compare with the baseline. Every new or changed path
   must belong to the change: delete scratch files, debug scripts, logs and output you created, and
   remove debug prints, commented-out code and temporary TODOs you added. New source and test files
   stay. Never run `git add`, `commit`, `stash`, `reset`, `checkout`, `clean` or `push`; committing
   is the `git-commit` skill, and only on the user's request. Without git, list every file you
   touched instead. In a team assignment, follow the role protocol for explicitly authorized
   local worktree commits; shared-directory teammates still never change the shared Git state.
8. Lead the final answer, in the user's language, with the result and why it matters in one or two
   sentences. Follow with only the material implementation choices, a compact verification result
   and anything the user must act on or could not verify. Group related changed files behind links
   instead of giving every file its own explanation; call out new untracked files that must be added
   to git. Give exact commands, counts and a file-by-file account when the user requests that detail
   or it is needed to explain a failure.

A follow-up on this task (a correction, "also do X", a failing case) continues from step 5 and
repeats steps 6 to 8 for the new edits. A failure unrelated to the change goes to `code-bug-fix`
only when the user asks for it.
