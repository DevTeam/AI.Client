# Chat resources and reviews

Status: File and directory references and reviews of saved file changes are implemented. Reviewing selected text in earlier chat messages is the next increment.

## Product rules

- Files, directories, and reviews are resources. A message stores ordered typed references separately from its text. File bytes, directory listings, review comments, and diffs are never pasted into the stored message body.
- A review is a named, mutable resource of one chat, shared across its branches. It is based on the saved changes of one completed assistant step. Its source message ID remains fixed; its name, selected files, and comments can change. Reviews have no user-visible version history. The numeric revision is only an optimistic concurrency check for saving edits.
- The chat's left sidebar lists all created reviews after Directories, sorted by the source step's date. It does not show branch labels or unreviewed steps. Any review may be opened and edited from any branch of that chat.
- The composer `+` menu lists created reviews and offers Create review. Picking an existing review attaches its link. A new or edited review is automatically attached to the current composer draft. The user can remove that link without deleting the review. Opening a review without changing it does not attach it.
- A review may contain selected files and zero comments, to ask the assistant to review those changes. No review with zero selected files can be saved.
- A sent or queued message continues to reference the same mutable review. Edits to its comments or name are visible wherever that resource is opened, including from earlier sent turns. Edits do not start a model run; the next model request resolves the current state. Earlier model requests cannot be reconstructed from the mutable resource alone.

## Entry points and layout

`Review` on a saved `WorkspaceChanges` card opens the review workspace filtered to that step. The sidebar Reviews heading opens the chat's whole review list; an item opens that named review. `+ -> Review` opens the list with an add-to-composer action. Creation offers only completed change-bearing steps on the current branch, newest first. The list of already created reviews includes all branches of the chat.

The review workspace occupies the area to the right of the project sidebar. Its wide left area shows the saved diff, one file after another; the right area is a filterable file tree with checkboxes and comment counts. Selecting a file scrolls to its diff. A `+` in a diff line opens a comment editor below that line. Shift-click on another line of the same side selects a range. Each saved comment has inline Edit and Delete actions. Binary files and files without a saved text diff allow file-level comments. A file with comments cannot be unchecked until its comments are removed.

The header shows the review name, source step, list navigation, Add to message, and Close. Add to message remains useful after a user removed an automatically attached chip. The chat and queue show the review as a resource link, alongside file and directory links. The link opens the review workspace. Queueing a message does not freeze the review; it is resolved when the queued model request starts.

## Current model and storage

```text
ChatResourceRef: Id, Kind = File | Directory | Review, Path, Name?
ChatReview: Id, ProjectId, ChatId, Name, SourceMessageId,
            SourceCreatedAt, Files[], Comments[], CreatedAt, UpdatedAt, Revision
ReviewComment: Id, Path, OldStart/OldEnd?, NewStart/NewEnd?, Body
```

For a file or directory, `Path` is the canonical live workspace path and `Name` is absent. For a review, `Path` is empty and `Name` is its display label. The authoritative review and its comments are stored in the chat's project catalog, separate from chat messages. The source diff is read from the existing saved `WorkspaceChanges` on the assistant message; the review does not duplicate it. Each comment targets a file, an old-side line range, or a new-side line range. Context lines use the new side. The Host checks that the source and selected files belong to the chat and that anchored lines exist in the saved diff.

`IReviewService` owns creation, listing, updates, and chat/project cleanup. `IReviewRepository` writes the per-project review catalog atomically and checks expected revisions. `IResourceService.ValidateForChatAsync` validates attached file/directory refs against project read grants and review refs against the target chat. `IResourceModelProjection.ProjectAsync` reads the current mutable review and produces a bounded model manifest with selected paths, anchors, and user comments, without full file contents or diffs. Earlier user messages containing the same review ID also resolve to its current state when building a new model context.

The Host exposes list/get/create/update review routes under `/api/projects/{projectId}/chats/{chatId}/reviews`. The model-facing App tools expose `app_read Reviews/Review`, `app_resources CreateReview/UpdateReview`, and `app_runs Submit` with the ordinary resource ref. The UI uses the same application services through HTTP and does not invoke MCP permission prompts for its own actions.

## Future: comments on chat messages

Message review will reuse the named chat review resource, automatic composer attachment, shared chat list, mutable comments, and model projection. The source becomes an earlier user or assistant message instead of a saved change set. Selecting any text fragment in the chat opens an inline comment editor and records the source message ID, a canonical visible-text extraction version, offsets, the selected quote, and short surrounding context. The Host verifies the anchor before displaying it; if rendering changes break the match, the UI keeps the comment and offers reattachment rather than moving it silently. A whole-message comment needs only the source message ID. Users can add, edit, and delete such comments directly in the chat; every saved edit attaches that review link to the current composer draft, where it can be removed.

## Limits and follow-up

- Review comments are mutable in place by product choice. Existing sent messages keep their reference, and the next model run sees the latest state. UI should make this shared behavior clear when a review appears in several messages.
- A review's historical diff may be unavailable or approximate. The UI must say why line comments cannot be made and still permit file-level comments.
- Draft resource links in the composer currently survive chat/branch switches within the running app, but are not persisted across an app restart. Persisting these links alongside text drafts remains follow-up work.
- Full message-text review, including selection mapping and anchor recovery, remains a separate increment.
