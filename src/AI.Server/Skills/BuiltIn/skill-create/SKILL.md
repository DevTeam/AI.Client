---
id: skill-create
name: Skill create
kind: playbook
description: Create a User or Project skill from the user's description: interview, draft SKILL.md and save it after review.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"What the skill should do, in the user's words"},"scope":{"type":"string","enum":["User","Project"],"description":"Only when the user said"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_skills","skill_search"]
---

Skill conventions:
- `id` is `<domain>-<action>[-<object>]` in lowercase kebab case. Domains: chat, project, memory,
  skill, instructions; start a new domain only for a new area such as `git` or `doc`. The action
  is a verb: create, rename, compact, fork, add, remove, review, save, edit.
- `name` is the id in words with the first letter capitalized: `project-directory-add` becomes
  "Project directory add".
- `description` is one sentence that starts with a verb and names every side effect ("… after
  confirmation", "changes nothing").
- `kind: generic` turns its parameters into JSON in an isolated model without tools and needs a
  `result` schema. `kind: playbook` is followed by the calling model with its ordinary tools,
  lists them in `tools` and has no `result`. Prefer a playbook whenever the skill reads or
  changes application data or files.
- `parameters` is a one-line JSON Schema object with `additionalProperties` false; keep optional
  everything the playbook can ask for.
- A playbook body is numbered steps. Every change is confirmed with `ask_user` unless the exact
  value came from the user; the recommended option comes first with " (Recommended)"; say what
  dismissed, expired and interrupted answers do; finish with a one-line report without ids or
  revisions.
- Write the body in English. Quoted labels in it are examples: the calling model writes questions,
  options and answers in the user's language.

1. List existing skills with `skill_search`; if one already covers `goal`, offer `skill-edit`.
2. Ask in one `ask_user` call only for what is still missing: the scope (User for every project,
   Project for this one), the inputs, and whether it may change data.
3. Draft the full SKILL.md: frontmatter lines `id`, `name`, `kind`, `description`, `parameters`,
   then `tools` or `result`, then `---` and the body.
4. Show the draft in your answer and call `ask_user` labelled "Save" with "Save (Recommended)" and
   "Change something"; a typed answer is the change to make, after which you ask again. Dismissed
   saves; expired or interrupted leaves the draft unsaved in your answer.
5. `app_skills` Save with the content, the scope, revision 0 and enabled true. On a validation
   error, fix the draft and save again.
6. Answer with one line: the id, and that it is now in the `/` list.
