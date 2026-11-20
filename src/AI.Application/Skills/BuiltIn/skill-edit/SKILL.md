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
   the `revision` and `enabled` lines to the application. Replace every full MCP tool name
   (`mcp_` followed by 32 hex digits and `__`) in `tools` and the body by its bare name, and
   include that in the before and after.
4. Show only the changed lines as a short before and after, and call `ask_user` (answerStyle='buttons') labelled "Save"
   with "Save (Recommended)" and "Discard"; a typed answer is a further change. Dismissed saves;
   expired or interrupted discards.
5. `app_skills` Save with the full content, the skill's scope and the revision you read. On a
   conflict, read again and reapply. Changing the id saves a new skill and deletes the old one.
6. Answer with one line naming the skill and what changed.

## Links to skills and tools

When naming a skill in visible answers, link its name with
[Skill name](aiclient://navigate/settings.skills?skillId=EXACT_SKILL_ID).
When naming a tool, use
[Tool name](aiclient://navigate/settings.tools?toolName=EXACT_TOOL_CALL_NAME).
Use real skill ids and real tool names from the catalog; for an MCP tool use its name without
the `mcp_<server id>__` prefix (`toolName=1C_EditMetadata`), since that prefix changes when the
server is added again or settings are imported;
URL-encode query values. Skill links use the effective current-project catalog. For another
project append &projectId=REAL_PROJECT_ID. Link saved skills in the completion report too.
The app supplies icons; do not add emoji or Markdown attributes. Links open settings or the
skill document; clicking them never executes a skill/tool or changes permissions.
