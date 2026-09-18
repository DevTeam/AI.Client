# Composer rules

Enter sends the message, Ctrl+Enter queues it, Ctrl+Alt+Enter creates a branch, Ctrl+Shift+Enter interrupts the current run and sends the message first. The send button performs whichever action is highlighted in its tooltip, so the keys and the button always mean the same thing. Fork and Replace pass the server the branch point or the message being replaced. Replace always interrupts the work of the selected branch, discards its old queue and starts the replacement immediately.

During generation the composer stays available: Enter puts the message at the tail of the queue, Ctrl+Shift+Enter answers it immediately. The composer performs a single Submit command. The server chooses the context, changes the history and manages the queue. The Web does not delete the subtree before sending and does not duplicate the CLI execution.

`↑` and `↓` without modifiers scroll through the project's history of sent messages, `Esc` leaves it
and returns the typed text. The key goes into the history only on the last line of the field, so a
multiline message stays navigable. Editing an inserted entry returns the field to
a normal draft. Details — in [UX decisions](12-ux-decisions.md#история-ввода).

The selected branch is passed as a stable ID regardless of the temporary view position during editing. Replacement uses the chat revision. An error leaves the text for fixing and resending.
