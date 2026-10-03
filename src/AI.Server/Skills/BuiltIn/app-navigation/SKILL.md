---
id: app-navigation
name: App navigation
icon: link
kind: playbook
description: Open or show application settings, connections, tools, skills, memory, widgets, projects, chats and branches; provide clickable application navigation links. Открыть настройки или перейти к разделу приложения, проекту, чату, ветке.
parameters: {"type":"object","properties":{"destination":{"type":"string","description":"The section, project, chat or branch requested by the user."}},"additionalProperties":false}
tools: ["app_navigate","app_read","ask_user"]
---

For a direct navigation request, perform it in this turn. Do not replace opening a section with
directions, a guide, interest questions or a claim that the user must click it themselves.

1. Settings: call app_navigate with target=settings, action=show. Connections:
   settings.connections; tools: settings.tools; skills: settings.skills; memory: settings.memory.
   Omit comment, waitForContinue and waitForUser for a direct request. These show actions open
   the containing panel. For other destinations discover targets with action=targets.
2. For a named project/chat/branch read application data to resolve its real IDs. Open it with
   app_navigate using projectId, chatId and branchId as appropriate. Never invent IDs or create
   objects to satisfy a navigation request. Ask only if the destination is genuinely ambiguous.
3. Check the tool outcome. Confirm briefly in the user's language only when outcome=applied.
   Report unavailable, stopped or expired accurately; do not pretend a transition happened.
4. For "where is" questions or reusable shortcuts, provide Markdown links:
   [Settings](aiclient://navigate/settings),
   [Connections](aiclient://navigate/settings.connections),
   [Chat](aiclient://navigate/chat?projectId=REAL_PROJECT_ID&chatId=REAL_CHAT_ID).
   A branch link uses target=branch and also includes branchId. A project link uses target=project
   and projectId. Links may open catalog targets with a Section or widgets; they cannot send,
   create, click arbitrary controls or edit values. Never include action, value or credentials.
   A link is activated by the user; supplying it does not fulfill a request to open something now.

Use app-guide skills only when the user asks for an explanation or a tour.
