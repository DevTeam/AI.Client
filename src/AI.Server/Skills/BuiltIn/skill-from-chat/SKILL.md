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
