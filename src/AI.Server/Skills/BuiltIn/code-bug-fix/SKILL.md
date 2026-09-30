---
id: code-bug-fix
name: Code bug fix
icon: bug
kind: playbook
description: Find and fix the root cause of a bug, error or failing test: reproduce it, add a test that fails, fix, rerun the tests, remove leftovers and summarize; never commits.
parameters: {"type":"object","properties":{"problem":{"type":"string","description":"The symptom, error text or failing test, in the user's words"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","write_file","edit_file","create_directory","delete_file","process_run","ask_user","spawn_subtask","run_skill"]
---

The project instructions and instruction files win over these steps where they differ.

1. Baseline. When the repository root has `.git`, record `git status --porcelain=v1 -uall` with
   `process_run`; paths already changed are the user's and stay untouched.
2. Reproduce. Turn `problem` into a command that shows the failure: the failing test, the build
   error, or a small program run. Run it and read the whole output. When it cannot be reproduced,
   say what you ran and ask the user with `ask_user` for the missing step, input or environment.
3. Find the root cause. Follow the stack trace or the data back to where the wrong value or state
   starts; read the code, its recent callers and tests. Form one hypothesis at a time and check it
   with evidence (output, a narrow test, a read), not by editing until the symptom goes away.
   After three hypotheses fail, stop and report what you ruled out instead of guessing further.
4. Write a failing test for the cause where the project has tests, and run it to see it fail for
   the right reason.
5. Fix the cause with the smallest change, not the symptom: no catch-all exception handlers,
   sleeps, skipped checks or special cases for the test's input. Leave unrelated code alone.
6. Verify: run the new test, the tests around the change, and the build. Read the output and exit
   code; claim only what you ran.
7. Clean up as in `code-feature-implement`: compare the status with the baseline, delete scratch
   files and debug output you created, remove temporary logging. Never stage, commit, stash, reset
   or push.
8. The final answer contains, in full: the root cause in one or two sentences, the fix, each
   changed file as a link, the commands run with their results, and anything you could not verify.
