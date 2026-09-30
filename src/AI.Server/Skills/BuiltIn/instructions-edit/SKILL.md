---
id: instructions-edit
name: Instructions edit
icon: scroll
kind: playbook
description: Change the current project's instructions as the user describes; the user reviews the new text before it applies.
parameters: {"type":"object","properties":{"change":{"type":"string","description":"What to add, change or remove, in the user's words"}},"additionalProperties":false}
tools: ["app_read","app_instructions"]
---

1. Take the change from `change` or from the user's message. Read the instructions with `app_read`
   resource=Instructions and keep the `revision`.
2. A single fact or personal preference belongs in memory: say so and suggest `memory-save`,
   unless the user insists on the instructions.
3. Make the smallest edit: keep the user's wording, structure and language, put new rules next to
   related ones, and remove rules the change contradicts.
4. `app_instructions` with the complete new text and the revision. The application shows the user
   the change before it applies, so do not ask separately.
5. Answer with one line summarizing the change; it applies from the next run.
