---
id: skill-edit
name: Skill edit
icon: wand
kind: playbook
description: Change a User or Project skill as the user describes and save it after they review the change.
parameters: {"type":"object","properties":{"skill_id":{"type":"string","description":"The skill to change"},"change":{"type":"string","description":"What to change, in the user's words"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_skills"]
---

1. Find the skill with `app_read` resource=Skills; with `skill_id` as the query you get its full
   SKILL.md, source and revision. Without it, list the skills and ask with `ask_user` which one.
2. Built-in skills are read-only and their ids are reserved. To change one, offer to save a copy
   under a new id in the User scope.
3. Apply `change` and keep the conventions from `skill-create`: the id system, an `icon` from
   its list, one-line JSON schemas, and playbooks that list `tools` and have no `result`. Leave
   the `revision` and `enabled` lines to the application.
4. Show only the changed lines as a short before and after, and call `ask_user` labelled "Save"
   with "Save (Recommended)" and "Discard"; a typed answer is a further change. Dismissed saves;
   expired or interrupted discards.
5. `app_skills` Save with the full content, the skill's scope and the revision you read. On a
   conflict, read again and reapply. Changing the id saves a new skill and deletes the old one.
6. Answer with one line naming the skill and what changed.
