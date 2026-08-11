# Concurrent chat runs

## Branch-level execution

Each branch owns an independent run and queue identified by `(chatId, branchId)`. `branchId` is mandatory: the original branch uses `chatId`, while a derived branch uses its stable root message ID. Branch runs may stream concurrently. Writes to the shared chat-history document are serialized briefly and always use the latest revision, while endpoint requests remain parallel.

For a branch being created, WebAssembly allocates the root message ID before enqueue. That ID is used both as the mandatory `branchId` and as the ID of the first user message persisted by the worker. Run identity therefore never changes after completion and requires no temporary anchor or alias.

Run lifecycle follows the current message graph. Valid run IDs are the original `chatId` plus the current alternative user-message roots. After graph mutations the Host reconciles persisted and in-memory runs against that set. Deleting a project or chat removes all owned runs; deleting or replacing a branch also removes runs whose roots are no longer branches, including nodes that ceased to be branch roots after a sibling disappeared.

## Accepted architecture

- Host owns one independent run state and sequential queue per chat. Switching chats, reloading, or closing WebAssembly does not cancel work.
- Mutating commands carry a UUID `operationId`; replaying the same command has no second effect.
- Run state is stored atomically in `<chatId>.<branchId>.run.json` beside chat history. A persisted `Generating` run restores as `Interrupted`, retains partial content, and leaves its queue paused.
- WebAssembly receives a complete snapshot on SSE connection/reconnection and subsequent state events; the server does not replay an event log.
- One run executes per branch. Messages submitted while it runs enter an editable, reorderable queue. Stop interrupts the run and pauses that queue.
- Chat indicators: generating spinner, unread blue dot, error red icon, interrupted square. Project indicators aggregate with priority `error`, `generating`, `unread` and expose all states in a tooltip.
- A response becomes read only when its chat is open and the application window is focused.
- Browser notifications are opt-in from Settings, stored per browser, shown only while unfocused, and contain project/chat names plus a private status without response text.

## Delivery stages

1. Persistent run state and idempotent queue commands.
2. Host dispatcher and HTTP/SSE snapshot contracts.
3. Per-chat WebAssembly state, queue editor, and sidebar indicators.
4. Focus/read tracking and opt-in system notifications.
