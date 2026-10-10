---
id: chat-summary
name: Chat summary
icon: list-checks
aliases: ["summary","sum"]
kind: playbook
description: Summarize this chat or another one in the project: decisions, open tasks and touched files; changes nothing.
parameters: {"type":"object","properties":{"chat_id":{"oneOf":[{"const":"current"},{"type":"string","format":"uuid"}],"description":"current or a chat id from this project"},"length":{"type":"string","enum":["short","full"],"description":"short is up to five bullets; full adds sections"}},"additionalProperties":false}
tools: ["app_read"]
---

1. For `current` or no `chat_id`, summarize the conversation you can see. For another chat, read
   it with `app_read` resource=Messages; follow the cursor only while the messages add something.
2. Answer in the conversation's language. `short` (the default): at most five bullets covering the
   goal, the outcome and what is still open. `full`: the sections Goal, Decisions, Done, Open and
   Files and commands, each only when it has content.
3. Quote ids, paths and commands exactly, and never report progress that the messages do not show.
   This skill is read-only, which makes it a safe example when the user asks to test skills. It
   never offers to delete the chat, however little the chat holds: the summary is the deliverable.
