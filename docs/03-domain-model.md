# Domain model

A `Project` stores its name, security settings, and `ConnectionId`. Connections and secrets are not duplicated in projects.

A `ChatThread` contains immutable messages and a `ChatBranch`. A message has an ID, role, text, time, and `ParentId`. A branch has a stable ID, `HeadMessageId`, name, `ParentBranchId`, and `RootMessageId`. The main branch's ID matches the chat ID.

The AI context is built along the parent chain of the selected head, without sibling branches. Fork creates a new branch. Replace deletes a subtree and adds a new message in a single chat entry.

`ChatRunState` stores the queue, status, error, revision, and processed `OperationId` values. Recovery explicitly transitions an incomplete generation to `Interrupted`; reading the state changes nothing.
