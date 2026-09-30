---
id: memory-review
name: Memory review
icon: eye
kind: playbook
description: Tidy long-term memory: merge duplicates, resolve contradictions and drop stale entries the user approves.
parameters: {"type":"object","properties":{"scope":{"type":"string","enum":["User","Project","All"],"description":"Which memory to review; All by default"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_memory"]
---

1. Read every entry with `app_read` resource=Memory, following the cursor; keep ids and revisions.
   Limit it to `scope` when given.
2. Find duplicates to merge, contradictions to resolve, entries in the wrong scope, entries that
   hold several facts and entries that are plainly out of date. Leave everything else alone.
3. With nothing to change, say so and stop. Otherwise call `ask_user` with one `multiSelect`
   question labelled "Apply": one option per proposed change, the label a short verb phrase such
   as "Merge 2 entries on code style" and the description the resulting text. Dismissed, expired
   or interrupted applies nothing: the user decides about their own memory.
4. Apply the chosen changes with `app_memory`: a merge updates the kept entry and deletes the
   rest; a move creates the entry in the new scope and deletes the old one.
5. Answer with one line counting updated, deleted and moved entries.
