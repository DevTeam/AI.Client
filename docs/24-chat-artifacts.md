# Chat resources and reviews

Status: File and directory references, diff reviews, and reviews of selected chat text are implemented.

## Product rules

- Files, directories, and reviews are resources. A message stores ordered typed references separately from its text. File bytes, directory listings, review comments, and diffs are never pasted into the stored message body.
- A review is a named, mutable resource of one chat, shared across its branches. A diff review is based on the saved changes of one completed assistant step. A message review is anchored to selected text in a user question or assistant answer. Its source message ID remains fixed. Reviews have no user-visible version history. The numeric revision is only an optimistic concurrency check for saving edits.
- Reviews are absent from the project sidebar. A chat's popup menu lists all of its reviews. A branch's popup menu lists reviews whose source change is in that branch or an ancestor. Both menus open a review in the right panel.
- The composer `+ -> Review` submenu lists created reviews, with a review of the previous completed round first when one exists and all other reviews ordered by latest edit. Five rows are visible before vertical scrolling. Picking one attaches its link. Review creation is available only from the `Review` button on a saved changes card.
- Creating or editing a review does not attach it to the composer. Attaching a review is always an explicit action through `+ -> Review` or `Add to message` in the review panel.
- Message reviews are absent from those lists and from sent-message resource chips. Selecting text opens their inline comment editor; the comment list offers an explicit `Add to message` action.
- A review may contain selected files and zero comments, to ask the assistant to review those changes. No review with zero selected files can be saved.
- A sent or queued message continues to reference the same mutable review. Its link can be removed from a sent message without deleting the review or its comments. Edits to its comments or name are visible wherever that resource is opened. Edits do not start a model run; the next model request resolves the current state. Earlier model requests cannot be reconstructed from the mutable resource alone.

## Entry points and layout

`Review` on a saved `WorkspaceChanges` card opens the review workspace for that round and permits creating another review of those changes. Chat and branch popup menus open existing reviews. `+ -> Review` attaches an existing review to the composer without opening the editor.

The review workspace opens as a right panel beside the chat. Its wide left area shows the saved diff, one file after another; the right area is a filterable file tree with checkboxes and comment counts. Selecting a file scrolls to its diff. A `+` in a diff line opens a comment editor below that line. Shift-click on another line of the same side selects a range. Each saved comment has inline Edit and Delete actions. Binary files and files without a saved text diff allow file-level comments. A file with comments cannot be unchecked until its comments are removed.

Paths in rendered messages become links however the model wrote them: a `file:///` URI (which the base prompt asks for) or bare Windows path in a link, a relative link such as `[x](src/y.cs)`, a code span such as `` `src/Program.cs:12` ``, or a bare `C:\` path in prose. File URIs and Windows paths in links are usable at once; everything else becomes a link only after `POST /api/projects/{id}/resources/resolve` (`IWorkspacePathResolver`) confirms it. The resolver strips a line suffix, tries a relative path under each readable grant root in order, and reports only paths the project may read, with their canonical form and kind. Local and relative links are never followed. A right-click opens `Add to message` and `Copy path`; adding one creates the same reference as `+ -> File` or `+ -> Directory`, using the resolved kind when there is one.

`fileLinks.js` keeps this off the rendering path: it scans only message blocks that changed, 200 ms after the change and in 8 ms slices, batches up to 100 paths per request with a browser `fetch` (a batch marshalled through .NET blocked the page for ~150 ms in WebAssembly), and caches every verdict per project. Measured on 300 blocks with 340 distinct paths: 4 requests, all links in ~1.3 s, no long tasks.

The header shows the diff review name, source step, Add to message, and Close. The chat and queue show a diff review as a resource link, alongside file and directory links. The link opens the review workspace. Queueing a message does not freeze the review; it is resolved when the queued model request starts.

## Current model and storage

```text
ChatResourceRef: Id, Kind = File | Directory | Review, Path, Name?, ReviewKind?
ChatReview: Id, ProjectId, ChatId, Name, SourceMessageId,
            SourceCreatedAt, Files[], Comments[], CreatedAt, UpdatedAt, Revision,
            Kind = Diff | Message, MessageComments[]?
ReviewComment: Id, Path, OldStart/OldEnd?, NewStart/NewEnd?, Body
MessageReviewComment: Id, Start, End, Quote, Body
```

For a file or directory, `Path` is the canonical live workspace path and `Name` is absent. For a review, `Path` is empty and `Name` is its display label. The authoritative review and its comments are stored in the chat's project catalog, separate from chat messages. A diff review reads its source from the existing saved `WorkspaceChanges` on the assistant message; it does not duplicate the diff. Its comments target a file, an old-side line range, or a new-side line range. A message review stores text offsets and the selected quote for each comment. The Host checks that the source belongs to the chat, validates diff anchors against the saved diff, and bounds message text anchors and comment bodies. The browser checks message quotes against the current rendered text before highlighting.

`IReviewService` owns creation, listing, updates, and chat/project cleanup. `IReviewRepository` writes the per-project review catalog atomically and checks expected revisions. `IResourceService.ValidateForChatAsync` validates attached file/directory refs against project read grants and review refs against the target chat. `IResourceModelProjection.ProjectAsync` reads the current mutable review and produces a bounded model manifest with selected paths, anchors, and user comments, without full file contents or diffs. Earlier user messages containing the same review ID also resolve to its current state when building a new model context.

The Host exposes list/get/create/update review routes under `/api/projects/{projectId}/chats/{chatId}/reviews`. The model-facing App tools expose `app_read Reviews/Review`, `app_resources CreateReview/UpdateReview`, and `app_runs Submit` with the ordinary resource ref. The UI uses the same application services through HTTP and does not invoke MCP permission prompts for its own actions.

## Comments on chat messages

Message review is a second review resource type. It does not appear in chat or branch review lists, nor as a visible resource chip on a sent message. Selecting text in a saved user or assistant message opens an inline comment editor. The resource records the source message ID, visible-text offsets, the selected quote, and the comment. Clicking highlighted text or opening the message's comment list allows editing and deletion. Commented text uses the application's standard `--accent-color`. Highlighting checks the saved quote against the current rendered text before drawing, so a changed rendering does not silently mark a different fragment. The user can manually attach the review to the composer from its inline comment list. The list shows each comment as the quoted fragment over its text, ordered as in the message; edit and delete appear on the row under the pointer, and that comment's mark in the message is emphasized.

## Limits and follow-up

- Review comments are mutable in place by product choice. Existing sent messages keep their reference, and the next model run sees the latest state. UI should make this shared behavior clear when a review appears in several messages.
- A review's historical diff may be unavailable or approximate. The UI must say why line comments cannot be made and still permit file-level comments.
- Draft resource links in the composer currently survive chat/branch switches within the running app, but are not persisted across an app restart. Persisting these links alongside text drafts remains follow-up work.
- If a future Markdown renderer changes the visible text enough to break a saved anchor, the comment remains in the list but its highlight is omitted. An explicit reattachment flow is follow-up work.
