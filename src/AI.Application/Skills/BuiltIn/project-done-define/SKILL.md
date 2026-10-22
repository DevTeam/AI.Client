---
id: project-done-define
name: Project done define
icon: clipboard-check
kind: playbook
aliases: ["dod", "definition-of-done"]
description: Write or change the current project's definition of done — the checks a task with a checkable outcome must pass — as a section of the project instructions.
parameters: {"type":"object","properties":{"change":{"type":"string","description":"What to add, change or remove, when the user named it"}},"additionalProperties":false}
tools: ["app_read","app_instructions","ask_user","list_directory","search_files","read_text_file","read_multiple_files"]
---

The definition of done is a section of the project instructions that every run reads. When a task
has a checkable outcome, the assistant takes the items that concern it as the criteria of its plan,
checks each with a tool before answering, and marks one that does not concern the task as not
applicable. An item is useful only if a tool can check it. Write the section in the language the
instructions already use.

1. Read the instructions with `app_read` resource=Instructions and keep the `revision`. If they
   already have a `## Definition of done` section, this is a change to it: keep what the user does
   not ask to change.
2. Learn what the project already requires, read-only, when its directories are granted: AGENTS.md,
   CLAUDE.md, CONTRIBUTING, the CI workflows, the build and test setup and linters. Take checks the
   project already enforces; do not invent requirements it does not have.
3. Draft 3–10 items, each scoped to the kind of change it concerns and checkable by a tool:
   "For code changes: the build succeeds and the test suites pass (`dotnet test AI.slnx`)",
   "For UI changes: checked in a dark and a light theme and with a non-blue accent". Leave out
   anything vague ("code is clean"), a step of the work rather than a check of its result, and
   anything a run cannot check itself. With `change` given, apply only that.
4. Ask with `ask_user`, in one call: which items to keep (multiSelect, all recommended), with
   `allowOther` for items of the user's own. Declined stops here; dismissed keeps the draft.
5. Write the section as `## Definition of done`, opening with one line: "Criteria for a task with a
   checkable outcome; an item that does not concern the task is not applicable." Then one item per
   line. Replace an existing section in place, otherwise append it; change nothing else.
6. `app_instructions` with the complete new text and the revision. The application shows the user
   the change before it applies, so do not ask again. Answer in one line: how many items and that
   they apply from the next run.
