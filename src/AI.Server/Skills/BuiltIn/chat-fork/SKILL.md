---
id: chat-fork
name: Chat fork
icon: fork
kind: playbook
description: Start a new branch of this chat from an earlier message to try another approach without losing the current one.
parameters: {"type":"object","properties":{"message":{"type":"string","description":"The user message to redo, as the user described it"},"prompt":{"type":"string","description":"The new message for the branch, if the user gave it"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_runs","app_chats","app_navigate"]
---

1. Take `projectId`, `chatId` and `branchId` from output.context. Read the branch's messages with
   `app_read` resource=Messages and that branchId; keep each message's id, role and parent.
2. Pick the user message to redo: the one `message` describes, else the last user message before
   the approach went wrong. When that is ambiguous, call `ask_user` with one question labelled
   "Fork from": up to four recent user messages, newest first, each label a quote of at most 60
   characters.
3. The branch needs its first message. Use `prompt`; otherwise ask in the same `ask_user` call:
   "What should the branch try?" with `allowOther` on and the option "Repeat the same message".
   Dismissed, expired or interrupted: change nothing.
4. `app_runs` Submit with the chat id, mode Fork, `parentMessageId` = the message just before the
   chosen user message on the branch, the new content and wait false.
5. When the result names a new branch, `app_chats` RenameBranch it to a 2 to 5 word title of the
   new approach, reading the chat's revision first.
6. `app_navigate` to the chat and the new branch, so the user watches it run.
7. Answer with one line: which message the branch starts from and that it is running and open.
