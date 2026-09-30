---
id: project-create
name: Project create
icon: project
kind: playbook
description: Create a project from directories the user picks, with a suggested name and access level, then open its first chat and hand it the work the user asked for there.
parameters: {"type":"object","properties":{"name":{"type":"string","description":"The project name, only when the user already gave one"},"description":{"type":"string","description":"What the project is for, if the user said"},"task":{"type":"string","description":"Work the user wants done in the new project, such as code to write, in their words"}},"additionalProperties":false}
tools: ["ask_user","app_projects","app_security","app_chats","app_runs","app_navigate"]
---

1. Call `ask_user` with one question labelled "Directories" and `pathKind` "directories": "Which
   directories belong to the new project?". A project needs at least one; if the question is
   dismissed, expired or interrupted, stop and say that no project was created.
2. Suggest a name: the final segment of a single directory; for several, the final segment of
   their nearest meaningful common parent, or their distinct final segments joined with " + ".
3. Call `ask_user` once with the remaining questions:
   - "Name" (skip it when `name` was passed): the suggestion first as "<name> (Recommended)" with
     the description "From the selected directories", and `allowOther` on.
   - "Access": "Read and write (Recommended)" and "Read only".
   Dismissed answers take the recommended options.
4. `app_projects` Create with the name (without the recommendation suffix) and `description` when known.
5. For each directory, `app_security` AddDirectoryGrant with the returned project id and the
   revision from the previous step, a fresh grant id, `recursive` true, the final segment as the
   display name and toolNames ["read","write","edit","delete"] or ["read"]. Each grant returns
   the revision for the next one.
6. `app_chats` Create the project's first chat with the new project id. Its title names the task
   in two to five words in the user's language, or is the project name when there is no task.
7. When `task` is given, or the request asked for work inside the new project (code to write,
   files to create or examine), `app_runs` Submit it to the new chat with mode Send and wait
   false: the task in the user's words and language, plus what was settled here (names, paths,
   choices). Do not do that work in this chat: its tools cannot reach the new project's
   directories, while the new chat's run can.
8. `app_navigate` to the new project and chat, so the user continues there.
9. Answer with one line: the project name, how many directories, the access level, and that the
   work continues in the new chat, or that the chat is ready for the first message.
