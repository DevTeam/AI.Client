# Composer rules

Enter sends the message, Ctrl+Enter queues it, Ctrl+Alt+Enter creates a branch, Ctrl+Shift+Enter interrupts the current run and sends the message first. The send button performs whichever action is highlighted in its tooltip, so the keys and the button always mean the same thing. Fork and Replace pass the server the branch point or the message being replaced. Replace always interrupts the work of the selected branch, discards its old queue and starts the replacement immediately.

During generation the composer stays available: Enter puts the message at the tail of the queue, Ctrl+Shift+Enter answers it immediately. The composer performs a single Submit command. The server chooses the context, changes the history and manages the queue. The Web does not delete the subtree before sending and does not duplicate the CLI execution.

`↑` and `↓` without modifiers scroll through the project's history of sent messages, `Esc` leaves it
and returns the typed text. The key goes into the history only on the last line of the field, so a
multiline message stays navigable. Editing an inserted entry returns the field to
a normal draft. Details — in [UX decisions](12-ux-decisions.md#история-ввода).

The selected branch is passed as a stable ID regardless of the temporary view position during editing. Replacement uses the chat revision. An error leaves the text for fixing and resending.

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
