# Concurrent runs

SubmitChatMessageRequest passes OperationId, MessageId, Content and the mode Send, Queue, Fork, Replace or SendNow. BranchId determines the branch, ParentMessageId the branch point, ReplaceSourceId the root of the replacement. The Queue mode defers execution; there is no separate parallel flag for the queue state.

A repeated OperationId does not add the command a second time. The queue is persisted until the answer is committed. Branch generations run in parallel, chat changes go through ChatSynchronization. An HTTP request does not hold the lock.

SSE delivers the latest snapshots and may coalesce intermediate events. The client compares Revision and ChatRevision, keeps the selected branch ID and takes the head from the current chat.

The queue also stores an already sent command: its Stage is UserCommitted until the answer is committed. The snapshot publishes the whole list together with Stage and ActiveMessageId, and the client decides what to show as a queue row and what as turn state.

An interruption preserves what the model managed to write: the partial answer is committed as a separate message with IsIncomplete and a deterministic identifier. Repeating the same command does not continue it but replaces it — the branch is rolled back to the user message via RewindBranchTo, and the abandoned tail is deleted on prune. The partial answer has its own identifier, so the "answer already exists" check does not accept it and resuming after a restart does not close the command for nothing.

SendNow first stops the branch and waits for the worker, then takes the interrupted command off the queue and puts the new message first. Clear removes only unsent commands, ClearAll stops the run and clears everything, Discard takes one sent command off and unblocks the queue.

Stop cancels generation. Resume resumes the queue, including immediate resumption after Stop. When the Host stops, workers are cancelled and awaited. After a restart, unfinished work gets Interrupted. Deletion first finishes the workers.
