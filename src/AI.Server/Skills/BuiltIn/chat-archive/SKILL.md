---
id: chat-archive
name: Chat archive
icon: archive
kind: playbook
description: Archive or restore named chats, clean up a project's old conversations by date and time, or undo a specific archive operation while preserving history and branches.
aliases: ["archive"]
parameters: {"type":"object","properties":{"action":{"type":"string","enum":["archive","restore","cleanup","undo"]},"projectId":{"type":"string","format":"uuid"},"chatId":{"type":"string","format":"uuid"},"before":{"type":"string","format":"date-time","description":"Exclusive last-activity cutoff with an explicit timezone offset"},"archiveOperationId":{"type":"string","format":"uuid"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_chats"]
---

1. Use the user's request to choose archive, restore, project cleanup or undo. If they only invoked
   the skill, ask what to do with concrete choices. Use the current project from output.context
   unless they named another project; resolve project names through app_read Projects.
2. For a named chat, use app_read Chats (archiveScope All) to resolve its id, then read Chat for
   its current revision. Ambiguous names need a choice. Archive and Restore preserve the whole
   conversation, branches, settings and pin order. Call app_chats with the corresponding
   operation, projectId, chatId, revision and a fresh operationId. Explicit single-chat archiving
   also works during a run, including this chat; it preserves that run. Never stop a run just to
   archive it. Project cleanup always skips running chats and chats requiring attention.
3. For project cleanup, ask only for missing scope and cutoff in one ask_user call. Offer no
   activity for 7, 30 or 90 days, or a date and time. Specify which time zone applies and convert
   the answer to an ISO timestamp with an explicit offset. Do not guess a time zone. Dismissed,
   expired, interrupted or unanswered scope/cutoff means nothing is archived.
4. Preview with app_chats ArchiveBatch, activityBefore and dryRun true. Pinned chats are excluded
   unless the user explicitly included them. Running chats and chats requiring attention are
   skipped. Show the count, cutoff and time zone, and a short list of candidates. With no eligible
   chats, stop. Let the user narrow the list when requested; never add new chats after review.
5. Apply the selected ids and revisions from the preview as targets with ArchiveBatch and dryRun
   false. Select at most 500 per call. Supply confirmationText, confirmLabel and cancelLabel in
   the user's language, describing this exact selection and cutoff. The tool itself asks for an
   affirmative confirmation; do not ask again through ask_user. Cancellation, silence or timeout
   changes nothing. Never replace this bulk operation with a loop of single Archive calls to
   bypass its confirmation. If more than 500 chats were selected, explain that each batch needs
   its own confirmation and operationId.
6. For undo, use the archiveOperationId the earlier result returned. If it is unknown, list
   archived chats with app_read Chats archiveScope Archived, paging until the requested operation
   can be identified. Do not guess between batches. Call app_chats UndoArchive with the projectId,
   archiveOperationId and a fresh operationId. Already restored chats and chats archived again by
   a different operation remain outside that undo.
7. Report changed and skipped counts and their reasons. After archiving, include the operationId
   so the user can request undo later, including after restarting. Archive reads and searches use
   archiveScope Archived or All. Opening a chat leaves it archived; a new user message restores it.
