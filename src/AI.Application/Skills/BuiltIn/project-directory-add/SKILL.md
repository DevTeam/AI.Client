---
id: project-directory-add
name: Project directory add
icon: folder-plus
aliases: ["add-dir"]
kind: playbook
description: Grant the current project access to more directories, choosing read-only or read-write access.
parameters: {"type":"object","properties":{"paths":{"type":"array","items":{"type":"string"},"description":"Absolute directories the user already named"},"access":{"type":"string","enum":["read","readwrite"],"description":"Only when the user already said which access they want"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_security"]
---

1. Read the current project with `app_read` resource=Project and keep its `revision` and grants.
2. Ask what is still unknown in one `ask_user` call:
   - no `paths`: "Directories" with `pathKind` "directories": "Which directories should this
     project get access to?";
   - no `access`: "Access" with "Read only (Recommended)" and "Read and write". Recommend
     "Read and write" instead when the user asked to change files there.
   A dismissed, expired or interrupted access choice grants nothing. With no directories chosen,
   stop and say nothing changed. Do not ask again for exact paths and access already authorized.
3. Skip a path only when an existing recursive grant covers both the path and the requested
   capabilities. A read-only grant does not cover a requested write. Say which ones you skipped.
4. Add all remaining directories in one `app_security` AddDirectoryGrants call with the project id,
   the current revision and `directoryGrants`. Each item has a fresh grant id, `recursive` true,
   its final segment as the display name and toolNames ["read"] or ["read","write","edit","delete"].
   Do not replace the project's whole security document. On conflict, reread and recalculate only
   the still-missing grants; reconfirm only if the requested access would change.
5. File and process tools see the new access from the next step. Continue whatever task needed the access.
6. Answer with one line listing the directories and the access.
