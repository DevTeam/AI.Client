# Composer rules

Enter sends the message; while the branch is generating, it adds the message to that turn instead. Ctrl+Enter queues it, Ctrl+Alt+Enter creates a branch, Alt+Shift+Enter sends nothing: the text becomes the task of a schedule, and the Schedule widget opens with it, shown and unfolded; saving there schedules this chat, or creates a new chat with the task as its first message, and only then clears the composer ([Scheduled chats](35-scheduled-chats.md#from-the-message-box)). Esc stops the turn in progress. The keys follow the agents people already use (Claude Code, Codex): Enter steers the running turn and Esc stops it, so there is no separate "interrupt and send" chord — Esc, then Enter, does the same in two plain keys. Alt+Enter and Ctrl+Shift+Enter, once an aside and "interrupt and send now", now send as Enter does, so an old habit still delivers the message. The send button performs whichever action is highlighted in its tooltip, so the keys and the button always mean the same thing.

The tooltip is a table: an icon, the action and its keys aligned on the right — Send ("Add to the current turn" while the branch generates), Queue for later, Send in a forked session, Schedule instead of sending — with the action the held keys would take lit in the accent; while a turn generates, "Stop the turn · Esc" follows, never lit, since it is no way of sending. Shift+Enter for a new line is underneath. While a branch is edited or replaced it shows that one action. The button's own icon follows the held keys too, and every way of sending is the send arrow varied: a plus beside it to add to the running turn, two lines under it to queue, a forking arrow to fork, a clock to schedule; the same icons mark the tooltip rows. The icon swaps with a short entrance, none with reduced motion. Fork and Replace pass the server the branch point or the message being replaced. Replace always interrupts the work of the selected branch, discards its old queue and starts the replacement immediately.

During generation the composer stays available. Enter adds the message to the turn: the server marks it `JoinsTurn`, puts it ahead of what was queued for later, and the turn reads it after its next tool batch ([Asides and team messages](34-asides-and-team-messages.md)); a `trigger_wait` in flight ends early so the message is read now. A turn that ends before it reads the message leaves it as the very next turn. Ctrl+Enter queues the message behind the turn without stopping it; on a branch with nothing running it holds the queue (Paused), so several messages can be written before any runs, and Resume or the next sent message lets them go. Esc stops the turn — only when nothing in the composer takes the key first (the `/` and `@` lists, the suggested reply, history browsing) and not while the turn waits on an `ask_user` question, whose answer is being written there. A message sent after Stop starts afresh: the stopped turn keeps its user message and partial answer in the transcript but is not run again ahead of the new message; one sent while the stop is still unwinding waits for it. Resume is still how a stopped turn is continued. The composer performs a single Submit command. The server chooses the context, changes the history and manages the queue. The Web does not delete the subtree before sending and does not duplicate the CLI execution.

`↑` and `↓` without modifiers scroll through the project's history of sent messages, `Esc` leaves it
and returns the typed text. The key goes into the history only on the last line of the field, so a
multiline message stays navigable. Editing an inserted entry returns the field to
a normal draft. Details — in [UX decisions](12-ux-decisions.md#история-ввода).

The selected branch is passed as a stable ID regardless of the temporary view position during editing. Replacement uses the chat revision. An error leaves the text for fixing and resending.

## Suggested reply

After an answer the Host drafts the user's likely next message with `chat-reply-suggest` (see
[Skills](27-skills.md#built-in-skills)), and the composer shows it as grey italic text while the box
is empty. It is not the user's text: it never enters the draft, the input history or a send, and
Enter with only the suggestion on screen sends nothing. `Tab` or `→` (or a click on its `Tab` key
cap) turns it into ordinary text with the caret at the end; typing replaces it; `Esc` hides it
until the next answer; `↑` browses the history as usual. `Ctrl+Space` in an empty box asks for a
draft now, also when the automatic one is switched off or was hidden.

A draft belongs to the head of the branch on screen, in any branch, and only while that head is a
finished answer the composer would reply to: not while the branch generates, has queued messages,
waits for an approval or an `ask_user` answer, or while a fork or replacement is being prepared. When
the head changes the draft is dropped. The Host keeps the latest draft per branch in memory, so
switching chats and back shows it again; a Host restart forgets it. **Settings → Chat → Suggest a
reply after each answer** switches the automatic draft off, next to **Name new chats after the first
answer** for `chat-rename`. Both are stored on the Host, which makes the calls whether or not a
window is open.

## Skills from the slash list

A message whose whole text so far is `/` plus a word (`/`, `/co`) opens the list of the
project's enabled effective skills above the composer: name with the matched letters, description,
and source (Project, User, Built-in). A slash later in the text, or a path such as `/usr/bin`, is
plain text. Name prefixes rank first, then word starts, substrings, scattered letters and finally
description matches; skills picked this session come first among equal matches. `↑`/`↓` move
through the list (wrapping), `Enter` or `Tab` picks, `Esc` closes it until the text stops being a
command; the pointer picks without taking focus from the field.

The picked skill becomes a chip before the text, and the text after it is optional instructions for
the skill. One skill per message: picking another replaces it; `Backspace` at the very start of the
field or the chip's cross removes it. The chip travels as a `Skill` resource (`Path` = skill ID), so
the queue, drafts, edit-and-branch and replace keep it like any other reference. The Host checks at
submission that the skill is enabled in the project, and the message shows the chip above its text.
The model sees a line naming the skill and runs it with `app_run_skill` under the ordinary tool policy.
