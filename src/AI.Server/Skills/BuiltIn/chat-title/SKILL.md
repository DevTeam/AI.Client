---
id: chat-title
name: Chat title
description: Give a new chat a short, specific name after its first answer.
parameters: {"type":"object","properties":{"chat_id":{"type":"string","format":"uuid","description":"ID of the chat to name in the current project"}},"required":["chat_id"],"additionalProperties":false}
---

Read the chat with the `read_chat` tool before proposing a title. Choose the messages you need;
do not infer the topic from the chat ID or the provisional title. Return only one plain-text title
in the language of the conversation. Aim for 3 to 7 words and at most 64 characters. Describe
the user's task, not the assistant's process. Do not include quotes, Markdown, a trailing period,
or private file paths.
