---
id: chat-compact
name: Chat compact
icon: minimize
kind: playbook
description: Free the model's context: summarize this chat and continue in a new chat that starts from the summary.
parameters: {"type":"object","properties":{"focus":{"type":"string","description":"What the summary must keep, if the user said"},"next":{"type":"string","description":"The task to continue with in the new chat, if the user said"}},"additionalProperties":false}
tools: ["app_read","app_chats","app_runs"]
---

Running this skill is the user's request to move on with a smaller context, so do not ask
whether to do it. The old chat stays as it is; the model context shrinks because the new chat
starts from the summary alone. Use `chat-context-compact` instead when only the finished work of the
current turn is too large.

1. Take `projectId` and `chatId` from output.context. Read the chat with `app_read` resource=Chat
   for its title. Use the conversation you already see, and read older messages with
   resource=Messages only if they fell out of your context.
2. Write the summary in the conversation's language, at most about 600 words, under short
   headings: goal; decisions and their reasons; facts, names, ids, paths and commands that later
   work needs; what was done and verified; open problems and next steps. Put `focus` first. Leave
   out greetings, dead ends that taught nothing, and secrets.
3. `app_chats` Create with the old title followed by a short "continued" marker in the title's
   language, for example "Deploy fixes (cont.)" or "Исправления деплоя (продолжение)".
4. `app_runs` Submit to the new chat with mode Send and wait false. The content is one line
   saying it continues «<old title>», then the summary, then the next step: `next` when given,
   otherwise an instruction to wait for the user's next message without doing anything.
5. Answer in this chat with the summary and one line naming the new chat, telling the user to
   continue there.
