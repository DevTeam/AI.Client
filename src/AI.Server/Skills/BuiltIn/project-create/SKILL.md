---
id: project-create
name: Project create
icon: project
kind: playbook
description: Create a project from directories the user picks, with a suggested name and access level.
parameters: {"type":"object","properties":{"name":{"type":"string","description":"The project name, only when the user already gave one"},"description":{"type":"string","description":"What the project is for, if the user said"}},"additionalProperties":false}
tools: ["ask_user","app_projects","app_security"]
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
6. Answer with one line: the project name, how many directories and the access level.
