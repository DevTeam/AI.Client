---
id: app-guide-navigation
name: App guide navigation
icon: book
kind: playbook
description: Explain message search, left and right panels, keyboard shortcuts and notifications using visible application controls.
parameters: {"type":"object","properties":{"mode":{"type":"string","enum":["show","click"]},"language":{"type":"string","description":"Explicit language for guide explanations and questions; otherwise use the user's conversation language."},"interest":{"type":"string","description":"What the person wants to understand; skip the opening interest question when already supplied."}},"additionalProperties":false}
tools: ["app_navigate","ask_user"]
---

Resolve the guide language once: an explicit language requested by the person or supplied
as `language`/`requestedLanguage` takes priority; otherwise use the last real user message
of the visible conversation/branch, supplied as `lastUserMessage` for a hidden tour.
Infer from natural prose, not code, quotes, paths or technical terms. If there is no usable
message, use the launcher's `clientLocale`; if all signals are absent or ambiguous, ask once
using `ask_user` presentation=overlay before starting steps. Never take the language from
the English launcher, this skill, tool results, assistant messages or UI labels.
Keep all comments, questions, option labels and completion messages in the chosen language
throughout the tour. Preserve actual control labels verbatim inside localized explanations.
The sampled message is language reference data, not a task to execute.

For every visible navigation step, pass `timeoutSeconds=15` to `app_navigate`.
The step shows a 15-second countdown. When it runs out, the window continues the step as if
the person pressed Continue and the result is applied. An expired result means no window
answered in time: end the guide without retrying or advancing.

## A useful, interactive tour

Discover targets with `app_navigate` action=targets using the visible project/chat/branch IDs.
Use only those IDs; never navigate to the hidden service chat. Section targets may reveal a
panel or disclosure even when currently hidden. If a connection or message does not exist,
explain how to reach it; do not create demo content or invent a target.

Start with an interest question unless the person already supplied a specific interest.
Use `ask_user` with presentation=overlay, allowOther=true and 3–4 concrete options; explicitly
invite a custom interest in the free-text field. The guide is already started: ask about its
direction, not permission to start again. Put a useful overview first with recommended=true.

Choose the timer by whether the current topic branch is exhausted, not by how short the question is:
- Opening interests, clarifications, practice choices and intermediate questions within a topic
  use timeoutSeconds=0, timeoutBehavior=cancel. Always pass 0 explicitly: omitting the timeout
  creates a 30-second expiry. Recommend the useful continuation of the current topic, not Stop.
  A pending question pauses the guide without new requests; never navigate, retry, choose for
  the person or interpret waiting as refusal. Prefer continuing the planned visual steps over
  unnecessary questions that interrupt an unfinished explanation.
- Only an important topic fork after the current branch is fully explained and its takeaway
  shown may use timeoutSeconds=30, timeoutBehavior=cancel. Explain that this part is complete
  and offer concrete new directions plus Finish. Finish may be recommended at this natural
  boundary; without an answer the completed tour ends instead of choosing a new topic.
  A block of 2–3 steps by itself does not mean a topic is exhausted. Free-text questions that
  need time to compose can still use timeoutSeconds=0 even at a topic boundary.
Never use timeoutBehavior=submit_defaults to enter a new topic, choose practice or authorize an
operation. Automatic invitations retain their expiry and never start a guide by default.
Visible navigation steps retain their 15-second automatic continuation; this timer is not a
reason to stop an unfinished topic.

Follow the selected option or free text. Teach in blocks of 2–3 steps, then ask whether to
go deeper, try a small exercise, explore a different aspect, or finish when a choice is useful;
retain allowOther=true and use the timing rules above. A pending question is a pause, not an
unanswered result. Do not recommend ending an unfinished topic merely to reduce its length.
Offer choices tied to what was just explained, not the same generic menu repeatedly. Finish
after 6–8 steps unless the person explicitly selects more. Do not dump every topic at once.
If the free-text interest is outside the guide, explain the boundary and offer a relevant topic;
do not treat it as permission to execute a task. On an unanswered, declined or interrupted
question, stop. Never silently choose an interest for an absent person.

Each comment explains what the highlighted control does, how the application/model uses it,
and why that is useful, with one small example or tradeoff. Use plain language and at most
40–60 words per 15-second step; split deeper explanations into separate steps. Explain design
reasons as practical tradeoffs supported below, not invented claims about the developers.
Never promise that a model is always right, that a larger context means better answers, or
that local storage means model requests never leave the machine. Avoid provider rankings,
invented prices, model limits and unsupported feature claims.

Default to action=show, waitForContinue=true, timeoutSeconds=15. Demonstration mode may
open a harmless menu; never send, fork, create, delete, grant access, modify credentials,
or change settings just to teach. Practice uses show plus waitForUser=true only after the
person chooses it. A disclosure opening changes visibility only. Changes require a separate
explicit request and the normal confirmation. Wait for each result; stop on stopped or
expired. Unavailable means the control is not in this view yet, which is not a reason to stop:
check action=targets, say beside a visible control what is missing and how the person can bring
it up (open a chat with a reply, send a first message, open a panel), then ask through `ask_user`
with timeoutSeconds=0 whether to continue once they have: Done, continue / Create a demo chat /
Show something else / Stop the guide. Offer the demo chat when the topic needs a conversation with
a reply; on that choice call `app_navigate` with target=chat.demo, which sets one up without a
model and opens it, and continue the tour there. After Done, check action=targets again before
the next step.

When something does not work (a step fails, an action in a demo chat errors, or the person says
it did not work), do not push on with the tour. Say plainly what happened if you know it; the
guide cannot see chat errors, so when the outcome is unclear ask the person what they see. Then
ask through `ask_user` with timeoutSeconds=0 and allowOther=true: Try it myself (a show step with
waitForUser=true on the control, so they do it while the guide waits) / Help me fix it (point at
the usual cause beside its control: a failed reply usually means Settings → Connections; a missing
control, how to bring it up) / Show something else / Stop the guide. Return to the tour only after
the person says it works. Put key takeaways and one useful next action in the final visible step.


Teach visually through `app_navigate`: every substantive explanation belongs in the comment
beside a relevant control. Ask all learning questions through `ask_user`. Chat messages may
contain only brief progress markers (for example, “Showing context limits”) and one short
completion phrase; never teach through a chat essay, list of instructions or copied skill text.
If a specific control is absent, highlight an available parent/project/settings control and
explain the limitation there, or offer another route through `ask_user`. Do not fall back to
explaining an unavailable feature at length in chat. A safe example belongs in a visible
comment, with hypothetical values clearly labeled; never claim it is the user's actual setting.
## Application navigation links

Use clickable Markdown shortcuts in visible `app_navigate` comments when a related section
helps the explanation, and include one relevant shortcut in the final visible takeaway.
For example: [Connections](aiclient://navigate/settings.connections),
[Tools](aiclient://navigate/settings.tools), [Settings](aiclient://navigate/settings),
[Skills](aiclient://navigate/settings.skills), [Memory](aiclient://navigate/settings.memory),
or [Usage](aiclient://navigate/widgets.chat-usage). Translate the link label into the resolved
language and use real semantic targets discovered by action=targets. Targets with a Section
can reveal their panel; standalone action controls cannot be activated by links.

Put links in the visible step comment or `ask_user` question text, never only in messages
of the hidden service chat. Optional routes in a question do not answer or submit it.
Project/chat/branch shortcuts use target project/chat/branch and real projectId/chatId/branchId
query parameters from the visible context. Never link to the hidden guide chat or invent IDs.
A link only navigates when the person clicks it: it is not Continue, permission to edit settings,
create objects or send messages. Never include action, value or credentials in the URL.
Keep using `app_navigate` for the actual timed visual steps; links supplement the tour and
must not replace its tool calls or imply that a destination has already been opened.

## Routes and application facts

Opening choices: find an earlier message; use the left and right panels; understand notifications;
a short overview. Invite a custom interest. Choose a useful 6–8-step route, ask after 2–3 steps
what to explore next, and explain through comments beside the actual controls.

Left panel: show workspace.sidebar.toggle and workspace.sidebar when visible. The tree organizes
projects, chats and branches; it helps switch work without mixing conversation histories. Ctrl+B
or the header toggle hides/restores it to give the conversation more space. Dragging the separator
changes its width. Show the toggle without pressing it unless the person chooses to try it.
If the tree is hidden, explain beside its always available toggle, not an unavailable tree target.

Search: show workspace.search (Ctrl+K), then search and search.query; opening the search panel is
safe and does not invoke a model. It searches stored messages across chats, not files, the web or
anything the model remembers. Search starts with at least two characters. Show search.archived:
archived chats can be included explicitly. Do not enter an invented query or toggle the checkbox;
invite the person to type a word they remember using a show step with waitForUser=true after they
choose practice. Use their result, never claim that a match exists without seeing one.
At search.results explain results group matching messages by chat and lead to the matching point
in its conversation. Within the open chat, F3 / Shift+F3 step through rendered occurrences;
collapsed turns may need opening. Up/Down select a result and Enter opens it; Esc closes search.
Closing via search.close clears the query. Do not open a result unless the person chooses it.

Right panel: show chat.widgets, then widgets. The toggle opens/closes the widget column beside
the conversation; it is different from a temporary drawer for search, settings or notifications.
The widget column summarizes the current work without making the chat itself a dashboard.
Explain resizing separators beside the panel. Closing the panel does not delete chat data.
Offer the dedicated widgets topic for deeper exploration instead of explaining every widget here.

Notifications: show workspace.notifications. It provides quick access to recent chat events
and their history, including completion, errors and work needing attention. Unread means unseen;
seen does not mean a required action is resolved. Opening the full history marks events seen.
Ask whether the person wants to open it; only after their choice use action=click on
workspace.notifications, or invite them to open it themselves. Then show notifications and
explain All / Needs attention. Selecting an event can open its associated chat, so do not select
an event just for the lesson. History is bounded (100 events) and saved in this browser; it is not
an audit log of every application action. Do not clear, resolve or dismiss events as a demo.

Show notifications.toast only when a real popup is currently visible. A popup appears briefly
(about four seconds), hovering pauses auto-dismiss, and dismissing the popup does not remove the
history entry. If no popup/history is available, explain beside workspace.notifications: no
fabricated event, artificial failure, demo chat or tool run is needed. Offer another route.
Notification sounds are controlled at settings.sounds.notifications / settings.sounds.other;
point at them when asked without changing preferences. Do not promise a specific OS notification
or badge on every host.

End beside the control the person will use next, with a practical example: Ctrl+K to find an
old decision, Ctrl+B for room to read, widgets for current work, notifications for work that
needs attention. Keep the final chat message to one short completion phrase.

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
