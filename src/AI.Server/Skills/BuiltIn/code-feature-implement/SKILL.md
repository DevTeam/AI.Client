---
id: code-feature-implement
name: Code feature implement
icon: code
kind: playbook
description: Implement a feature or code change end to end: explore, plan, edit files, run the build and tests, remove leftovers and summarize the change; never commits.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"What to build or change, in the user's words"},"scope":{"type":"string","description":"Files, modules or limits the user named"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","write_file","edit_file","create_directory","move_file","delete_file","process_run","ask_user","spawn_subtask","run_skill"]
---

The project instructions and instruction files (AGENTS.md, CLAUDE.md, CONTRIBUTING) win over these
steps where they differ: their build, test and style rules are the ones to follow.

1. Baseline. Find the repository root among the granted directories. When it has `.git`, run
   `process_run` with `git` and `["status","--porcelain=v1","-uall"]` in that directory, and
   `["rev-parse","--abbrev-ref","HEAD"]` for the branch. Paths already modified or untracked now
   are the user's work: never revert, reformat or delete them, and keep them out of the summary.
2. Explore. Locate the code with `grep_files` and `search_files` before reading; read the files
   the change touches, their callers, their tests and one similar existing feature whose patterns
   you will copy. For a broad search in an unfamiliar codebase, hand it to `spawn_subtask`.
3. Clarify only what is genuinely the user's decision and costly to guess (behaviour, public API,
   data format): one `ask_user` call, the recommended option first. Take the conventional choice
   for everything else and name it in the summary. A dismissed question takes the recommended
   option; an expired or interrupted one stops here with the plan as the answer.
4. Plan. When the change spans more than three files, write a short numbered plan (file and what
   changes) in your text before the first edit. Stay inside `goal` and `scope`: no unrelated
   refactoring, renames, formatting or dependency upgrades, and ask before adding a dependency.
   When the plan splits into substantial parts on disjoint files that could proceed at the same
   time, offer team-assemble in one sentence before implementing; do not start a team yourself.
5. Implement with `edit_file` for changes and `write_file` for new files. Match the surrounding
   code's naming, idiom and comment density. Add or update tests for the new behaviour where the
   project has tests.
6. Verify. Take the build and test commands from the instructions, README, CI files or manifests
   (package.json scripts, *.sln or *.csproj, pyproject.toml, Cargo.toml, go.mod, Makefile). Build,
   run the tests nearest to the change, then the wider suite when it finishes within the timeout.
   Read the output and exit code; on a failure fix the cause and run again, at most three rounds
   per failure. Never skip, weaken or delete a test to get green. Claim only what you ran: "should
   work" is not a result. When something cannot be run, say what and why.
7. Clean up. Run the status command again and compare with the baseline. Every new or changed path
   must belong to the change: delete scratch files, debug scripts, logs and output you created, and
   remove debug prints, commented-out code and temporary TODOs you added. New source and test files
   stay. Never run `git add`, `commit`, `stash`, `reset`, `checkout`, `clean` or `push`; committing
   is the `git-commit` skill, and only on the user's request. Without git, list every file you
   touched instead.
8. The final answer contains, in full, in the user's language:
   - what changed and why, in one or two sentences;
   - each changed file as a link with one line about it, new files marked, and untracked new files
     named as ones the user has to add to git;
   - the build and test commands run and their results (passed, failed, skipped counts), or what
     was not run and why;
   - assumptions and decisions you made, and anything left for the user.

A follow-up on this task (a correction, "also do X", a failing case) continues from step 5 and
repeats steps 6 to 8 for the new edits. A failure unrelated to the change goes to `code-bug-fix`
only when the user asks for it.
