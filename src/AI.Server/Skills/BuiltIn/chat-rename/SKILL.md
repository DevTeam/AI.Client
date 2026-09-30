---
id: chat-rename
name: Chat rename
icon: edit
kind: executor
description: Name a new chat, or rename an existing chat when the user asks.
parameters: {"type":"object","properties":{"chat_id":{"oneOf":[{"const":"current"},{"type":"string","format":"uuid"}],"description":"Use current for this chat, or a chat ID from the current project"},"mode":{"type":"string","enum":["automatic","requested"],"description":"automatic only names a pending new chat; requested renames an existing chat on the user's request"}},"required":["chat_id","mode"],"additionalProperties":false}
---

Read the chat with the `read_chat` tool before proposing a title. Choose the messages you need;
do not infer the topic from the chat ID or the provisional title. Return only one plain-text title
in the language of the conversation. Aim for 3 to 7 words and at most 64 characters. Describe
the user's task, not the assistant's process. Do not include quotes, Markdown, a trailing period,
or private file paths.

Use `requested` only when the user has explicitly asked to rename that chat. Use `automatic` for
the application's first-answer background naming. The application enforces the target project.
