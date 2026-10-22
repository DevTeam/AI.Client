---
id: skill-from-chat
name: Skill from chat
icon: message-circle
kind: playbook
description: Turn the workflow carried out in this chat into a reusable playbook skill, saved after review.
parameters: {"type":"object","properties":{"scope":{"type":"string","enum":["User","Project"],"description":"Only when the user said"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_skills","skill_search"]
---

1. Recover the workflow from this conversation: the goal, the steps that worked in order, the
   tools each one used, the decisions the user made and the checks at the end. Drop dead ends.
2. Turn the user's decisions into `ask_user` questions, and concrete names, paths and ids into
   parameters, so that the skill fits the next similar task rather than only this one.
3. Continue with steps 1 to 6 of `skill-create`, with this workflow as the goal, following its
   conventions: read them with `app_read` resource=Skills and query "skill-create".

## Links to skills and tools

When naming a skill in visible answers, link its name with
[Skill name](aiclient://navigate/settings.skills?skillId=EXACT_SKILL_ID).
When naming a tool, use
[Tool name](aiclient://navigate/settings.tools?toolName=EXACT_TOOL_CALL_NAME).
Use real skill ids and full tool call names including the MCP server prefix from the catalog;
URL-encode query values. Skill links use the effective current-project catalog. For another
project append &projectId=REAL_PROJECT_ID. Link saved skills in the completion report too.
The app supplies icons; do not add emoji or Markdown attributes. Links open settings or the
skill document; clicking them never executes a skill/tool or changes permissions.
