---
id: chat-context-compact
name: Chat context compact
kind: playbook
description: Replace the finished work of the current turn with a short summary only the model sees, or undo that; the transcript stays unchanged.
parameters: {"type":"object","properties":{"action":{"type":"string","enum":["compact","reset"],"description":"reset undoes the checkpoint of this turn; compact is the default"},"target_tokens":{"type":"integer","minimum":256,"maximum":4000,"description":"Summary size, only if the user named one; 1500 by default"}},"additionalProperties":false}
tools: ["context_compact"]
---

Running this skill is the request, so do not ask whether to do it. `context_compact` covers only
the work finished earlier in this turn: tool calls and results after the current user message.
It never edits stored messages or the visible transcript, and the checkpoint ends with the turn.

1. For `reset`, call `context_compact` action=Reset and say whether a checkpoint was removed.
2. Otherwise call `context_compact` action=Preview. When it has nothing to compact, say that this
   turn has no finished work yet and that `chat-compact` is the way to shrink the whole chat by
   continuing in a new one; do not run it unless the user asks.
3. Call `context_compact` action=Compact, passing `target_tokens` only when given. If Applied is
   false, report the guidance or error and continue without a checkpoint.
4. Report in one line how many messages and characters were replaced and the summary size, then
   go on with the rest of the user's request, if any.
