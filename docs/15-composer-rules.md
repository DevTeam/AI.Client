# Composer rules

Enter sends the message, Ctrl+Enter queues it, Ctrl+Alt+Enter creates a branch, Ctrl+Shift+Enter interrupts the current run and sends the message first. The send button performs whichever action is highlighted in its tooltip, so the keys and the button always mean the same thing. Fork and Replace pass the server the branch point or the message being replaced. Replace always interrupts the work of the selected branch, discards its old queue and starts the replacement immediately.

During generation the composer stays available: Enter puts the message at the tail of the queue, Ctrl+Shift+Enter answers it immediately. The composer performs a single Submit command. The server chooses the context, changes the history and manages the queue. The Web does not delete the subtree before sending and does not duplicate the CLI execution.

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
