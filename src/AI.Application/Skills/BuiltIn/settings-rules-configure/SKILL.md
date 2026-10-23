---
id: settings-rules-configure
name: Settings rules configure
icon: shield
kind: playbook
description: List, add, change or remove command rules that decide process_run calls by the command they run and file tool calls by the paths they change, globally, for the project or for this chat; use when the user asks to allow, ask about or forbid particular commands or paths rather than a whole tool.
parameters: {"type":"object","properties":{"request":{"type":"string","description":"What the user wants allowed, asked about or forbidden, in their words"},"scope":{"type":"string","enum":["global","project","chat"],"description":"Where the rules apply; omitted means the scope the user named, else project"}},"additionalProperties":false}
tools: ["app_read","app_security","ask_user"]
---

Command rules refine one built-in tool inside its own permission: the tool's policy stays the outer
bound (a denied tool stays denied), and within it a matching rule decides Allow, Ask or Deny for one
call. Write reports in the user's language.

- `process_run` (its rules also decide `process_run_unrestricted`, the same command without the sandbox): A word matches
  one argument exactly, or as a mask with `*` or `?` inside it; a lone `*` at the end matches any
  further arguments: `git status *`, `dotnet test *`, `git push --force *`. Git's options before the
  subcommand (`-C`, `-c`) are not part of the pattern; a forced push is always spelled `--force`.
- File tools `write_file`, `edit_file`, `create_directory`, `move_file`, `delete_file`,
  `delete_directory`, `zip_create`, `zip_extract`: the pattern is a path below the project's
  directories with `/` between folders: `docs/**`, `src/**/*.cs`, `build/out.log`. `**` stands for
  any number of folders; a full path matches only there. A call that names several paths (a move)
  needs all of them for Allow or Ask and any one for Deny.
- Deciding: a matching Deny always wins; otherwise the most specific pattern decides, and between
  equally specific ones the narrower scope (chat, then project, then global).
- The Host never lets an Allow through unasked when its guard stops the call: another program than
  the installed one, options that run other commands, paths outside the project, `.git`, files that
  may hold secrets. A shell or launcher, a command that can discard work and a deletion are allowed
  only by a pattern that spells them exactly. Say so when a requested Allow will still ask.

1. Read `app_read` resource=CommandRules (the global rules and the current project's and chat's),
   following every page, and resource=Project for the directory grants. When the user only asked to
   see the rules, list them grouped by tool with pattern, decision and scope, and stop.
2. Work out each rule from the request: the tool (`process_run` for a program, the file tool for a
   kind of change; "edits in docs" is `edit_file` and usually `write_file` too), the pattern, the
   decision and the scope. "Everywhere" or "in every project" is global; "here" or "in this project"
   is project; "in this chat" is chat; with no word for it, use the `scope` parameter, else project.
   Never invent what to allow: ask with `ask_user` when the commands or paths are unclear, offering
   concrete patterns as options. A dismissed or unanswered question changes nothing.
3. Prefer the narrowest pattern that serves the request: `git log *` rather than `git *`, `docs/**`
   rather than `**`. Do not widen an Allow to a whole program, a shell (`pwsh *`, `bash *`, `npx *`)
   or the whole project unless the user asked for exactly that. Adding an Allow, or removing or
   loosening an Ask or Deny, increases access: apply it only when the user asked for that change in
   this conversation. Adding Ask or Deny needs no confirmation.
4. Re-read CommandRules before writing; skip rules already present with the same decision. Apply each
   with `app_security` operation=SetCommandRule, a fresh operationId, `commandRule` holding `scope`
   (`Global`, `Project` or `Chat`), `pattern`, `decision` and `tool`, plus `projectId` for a Project
   rule and `projectId` and `chatId` for a Chat rule. Saving the same tool, scope and pattern again
   replaces the decision. Remove a rule with operation=RemoveCommandRule and its `ruleId`. Check
   `applied` and `error`; a refused pattern comes back with the reason: correct it once or ask. On an
   uncertain result read before retrying with the same operationId. Never give `app_security` itself
   Allow, and never change the tool's own policy to avoid a prompt.
5. Read CommandRules again and report each rule saved, changed or removed in one line: tool, pattern,
   decision, scope. Mention a rule that still asks because of the guard, and that a chat's or a
   project's narrower rule can override a global one. No ids.
