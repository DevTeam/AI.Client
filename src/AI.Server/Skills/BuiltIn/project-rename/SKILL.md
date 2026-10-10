---
id: project-rename
name: Project rename
icon: tag
aliases: ["rename"]
kind: playbook
description: Rename the current project: offer three names from its details and apply the one the user picks.
parameters: {"type":"object","properties":{"name":{"type":"string","description":"The exact new name, only when the user already gave one"},"hint":{"type":"string","description":"What the user wants the name to convey, if they said"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_projects","app_chats"]
---

1. Read the current project with `app_read` resource=Project. Keep its `revision`. Use its name,
   description and the display names of its directory grants to understand what it is for.
2. If `name` was passed, go to step 4 with it.
3. Make three candidates that follow these rules:
   - Keep the meaning of the current name: every idea it names must survive. "Sandbox - tool
     testing" still has to mention tools.
   - Keep the current name's language and script. Fix typos. Drop filler, paths, versions and
     separators such as " - ".
   - 2 to 5 words, at most 60 characters, no quotes, emoji or trailing period.
   - `hint`, when given, outranks everything but meaning.
   Call `ask_user` once with one question labelled "Name": the best candidate first with
   " (Recommended)" appended, then the other two, then "Keep current name" with the current name
   as its description. Leave `allowOther` on so the user can type their own.
   - Remove the recommendation suffix, in whatever language you wrote it, from the chosen label.
   - "Keep current name": change nothing and say so in one line.
   - Dismissed: use the recommended candidate. Expired or interrupted: change nothing and list the
     three candidates in one line.
4. Call `app_projects` Update with the project id, the revision and the new `name` only; omitted
   fields keep their values. Use a fresh operationId. On a conflict, read again and retry once.
5. Only when this work ran in a chat: once it is finished and reported, check with `app_read`
   resource=Messages whether this chat did anything besides it. When the chat holds nothing but the
   request and its report, ask once through `ask_user` whether to delete this chat, saying that
   deletion removes it together with all its branches and its whole history, with "Keep this chat
   (Recommended)" and "Delete this chat". A dismissed, expired, interrupted or unanswered question,
   or any other answer, keeps the chat. On delete, read this chat with `app_read` resource=Chat for
   its revision, then call `app_chats` Delete with `projectId`, this `chatId`, that revision, a fresh
   `operationId` and `dryRun` false; when the application refuses the deletion, report that and
   leave the chat.
6. Answer with one line: Renamed: «old» → «new», and whether the chat was kept. Do not mention
   revisions, ids, grants or policies.
