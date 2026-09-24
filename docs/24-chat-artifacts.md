# Chat artifacts and reviews

Status: Proposed design. No implementation is implied by this document.

## Goal and boundaries

A user can attach files, directories, and reviews to a chat turn without pasting their contents into the message. The same mechanism should admit later artifact kinds. A review can address changes made in earlier assistant turns, a whole earlier answer or question, or an arbitrary selection within one of those messages. The user can add, edit, and remove their own review comments.

The artifact is a first-class part of the user turn, not Markdown syntax. The composer, queue, transcript, and model request all refer to the same submitted artifact set. Merely opening a file or selecting text does not send anything to the model. A single application resource service is the authority for creating, resolving, reading, and revising project/chat resources; the UI and model tools call that service rather than implementing separate rules.

## Existing seams

- `ChatMessage` has immutable text, parent, role, and optional `WorkspaceChanges`; `ChatDocumentSerializer` writes message nodes and the chat manifest separately. `ChatContext` builds model context along the selected branch's parent chain.
- `WorkspaceChanges.razor` already shows a completed assistant turn's net file changes and saved diffs. A diff can be unavailable, approximate, truncated in the UI, or binary. It is evidence for **that turn**, not a representation of the current working tree.
- `SubmitChatMessageRequest`, queued messages, and `ChatComposerService` currently carry text only. The composer rejects an empty string. The proposed artifact list would travel with the same submit/queue/retry operation and make an artifact-only turn valid.
- Built-in filesystem tools already apply project directory grants. Attaching a path must not silently grant the agent access to it.
- The in-process `App tools` MCP server already separates read-only `app_read` from mutating tools because permissions are granted per tool. Its reads are paged and bounded; its writes use revisions and operation IDs.

## Resource service and model-facing tools

`IResourceService` is the shared application boundary. It owns scope validation, IDs, revisions, resolution, listing, retirement, and references from chat turns. Its typed operations are approximately `List(scope)`, `Get(ref)`, `ReadSource(ref, range)`, `Create(scope, target)`, `ReviseReview(id, expectedRevision, comments)`, and `Retire(id, expectedRevision)`. The Web API and MCP adapters call this service. It resolves historical review sources from saved chat nodes and validates live path references; current file contents remain available through the existing permitted filesystem tools. It is not a second filesystem implementation.

Expose this boundary through the existing App tools server:

| MCP surface | Operations | Policy |
|---|---|---|
| Extend `app_read` | `ProjectResources`, `ChatResources`, `Resource`, `ResourceSource` | Read-only; paged, range and character bounded |
| Add `app_resources` | `CreateProjectRef`, `CreateReview`, `ReviseReview`, `Retire` | Mutation policy; expected revision and operation ID |
| Extend `app_runs Submit` | Supply ordered resource refs with a user turn | Existing submit policy and idempotency |

This is one resource architecture, with two MCP permission surfaces. Combining reads and mutations in one callable tool would make every harmless review read require a write-capable approval, or allow review edits under a read approval. `app_resources` manages **application references and review comments**, not file contents, directory contents, project grants, or chat history. File editing remains with the existing filesystem tools and their grants. A resource is attached to a turn only by that turn's `Submit` operation, so there is no separate `Attach` write that can leave a half-submitted message.

The model may discover a project resource, resolve a chat review, read a bounded saved diff/quote, create a reference, or revise a review through these surfaces. The UI uses the same service without going through MCP permission prompts. Tool schema changes will change policy hashes, so the first implementation should introduce the resource operations as a coherent set.

## Terms and model

Keep one small envelope and kind-specific targets. Do not introduce a general artifact object with dozens of nullable fields or a parallel message type for each kind.

```text
UserTurn
  Text: string                    // may be empty when resources are present
  ResourceRefs: ResourceRef[]     // ordered, immutable when submitted

ResourceRef
  Scope: project | chat
  ResourceId: UUID
  Revision: number                // exact submitted version

ResourceDefinition
  Id, Scope, Kind: file | directory | review | ...
  Revision, Target, Label, CreatedAt, RetiredAt?

FileTarget      = ProjectId + canonical path relative to an allowed project root
DirectoryTarget = ProjectId + canonical path relative to an allowed project root
ReviewTarget    = ChatId + ReviewId

ReviewDocument
  Id, ChatId, Revision, CreatedBy, CreatedAt
  Sources[]                      // prior messages and/or prior change sets
  Comments[]                     // stable comment IDs, anchors, text, order
```

`ResourceRef` is a descriptor, not file bytes, a directory listing, a URL, or a serialized diff. Project resources hold reusable file/directory references; chat resources hold reviews tied to that chat. Store definitions in small per-scope manifests, with immutable revisions for submitted records. The only copies in message nodes are ordered IDs and revisions. Resolve through typed Host routes/actions; the UI never treats a label as a path or an executable link. The Host checks the target against the project/chat identity from the request, not a user-supplied absolute path alone. A retired resource remains resolvable for earlier submitted turns but is hidden from new pickers.

A file or directory ref is **live**: it names the workspace object at the time of a later read. Persist the canonical path and optional observed metadata (existence, size, modified time, hash for an already read small file) for stale-state warnings, but do not snapshot contents just because a chip was attached. A missing or moved target remains a visible, unresolved ref. A directory ref means "consider this directory"; it does not recursively enumerate or transmit its contents at send time.

A review ref is **historical**: it points to an immutable revision of a review document. Review sources are precise:

```text
MessageSource = ChatId + MessageId
ChangeSource  = ChatId + assistant MessageId + file key/path in that message's WorkspaceChanges

TextAnchor    = MessageId + canonical displayed-text version/hash
              + start/end offsets + selected quote + short prefix/suffix
DiffAnchor    = ChangeSource + old/new side + line interval + selected quote/context
```

The change source refers to the saved `WorkspaceChanges` of that assistant message, including its diff when available. It never silently switches to a fresh `git diff` or the current file. If a diff was not captured, review can target the file/change row as a whole; line comments are unavailable. A rename retains both old and new paths from the saved change. A change source is a reference to the existing saved change set, not a second copy of the diff in `ReviewDocument`.

Text anchors use the visible text as rendered from one immutable message, with a defined extraction/version algorithm. On selection, the client maps DOM text nodes back to offsets in that canonical visible text; offsets are Unicode scalar indices and are adjusted to grapheme boundaries. Save the exact selected quote and nearby context so the UI can verify the anchor after renderer changes. If the hash or quote no longer matches, show an `Unlinked selection` with the saved quote and offer reattachment; never silently move a comment to a different occurrence. A selection cannot span messages. A comment on an entire answer/question needs only `MessageSource` and no text anchor.

Comments have a stable ID, author, body, optional anchor, and status. The comment body is the user's own text; quotes are short locating aids rather than copies of the reviewed answer. Reject empty comments and bound quote/body sizes. A review may contain several sources and comments, so reviewing five changed files creates one artifact, not five new chat turns.

## Lifecycle and consistency

1. The user builds a **draft** in the composer or review panel. Draft refs/comments can be added, edited, reordered, and removed without changing chat history. Unsaved draft state is local to that chat/branch; a branch switch preserves it separately. The UI can call the resource service to create a reusable project ref; an unsent review stays a draft until submission.
2. Submit sends text plus ordered refs and any new review drafts in one operation. The Host validates access, source ancestry on the selected branch, anchor shape, size limits, and revisions, then persists new resource revisions and the immutable user message atomically from the reader's perspective. Queue entries retain the exact ref set and review draft payload; queue edit/removal changes that pending entry only. Retries use the original operation/message IDs and cannot create duplicate resources or reviews. A review created through `app_resources` before `app_runs Submit` can also be submitted by ref.
3. The submitted transcript renders text and artifact cards from structured data. The historical submitted review revision remains fixed even if the user later revises it. Forking retains refs in the shared ancestor; replacing or pruning a branch removes only references no longer reachable. Review documents referenced by any reachable branch or pending queue item remain retained.
4. To edit or delete a comment **after sending**, `Revise review` opens a draft from the latest review revision. The user changes it and sends an update as a new user turn/review revision. A deletion becomes a tombstone in that revision, with a visible `Comment removed` entry where needed. Earlier model runs and transcript turns retain the exact revision they saw; the next model run sees the new revision. The UI can show `Updated in a later turn` on the older card. No hidden mutation of past model input occurs.
5. A review remains readable if its file has changed or vanished: the saved change set and comments still exist. If its source message was removed by branch replacement and no reachable reference retains it, the reviewer sees an unavailable source and the saved short quote. The storage cleanup policy must account for cross-references before removing nodes.

On a revision conflict, reject the submit with a clear refresh/rebase action and preserve the draft. Chat message storage only needs the ordered refs. The resource service owns the small project/chat manifests and review documents. It must coordinate resource and message commits under the chat lease: create immutable resource records first, then publish the message/branch head; failed publication leaves unreferenced records that can be collected. Reading a submitted message must never expose a dangling ref. Avoid a global cross-project registry.

## Model context

Resource refs must be visible to the model even though they are not appended to `ChatMessage.Content`. A dedicated model projection resolves them through `IResourceService` and builds a short, deterministic manifest alongside each user message after `ChatContext` selects the branch. Example:

```text
User: Please address these comments.
Attached references:
- file: src/AI.Client.Web/Pages/Home.razor (live workspace path)
- directory: src/AI.Client.Server/Application (contents not loaded)
  - review: chat resource 5f... revision 2, based on assistant message a3...
  - src/.../Home.razor, new lines 341-345: "Handle the empty state."
  - answer a3..., selected "...": "This claim needs a source."
```

This is generated request content, never the stored chat text. The manifest contains user comments, source IDs, paths, line/selection metadata, and bounded quotes; it does not contain file bytes, directory trees, or full diffs. If the model needs current content, it uses an available read/search tool under the existing permission policy. If it needs historical changes, `app_read ResourceSource` returns the saved snippet or diff for the referenced source with a line/range selector and output limit. The model must be told whether it is looking at a historical diff or current workspace content. A missing permission or unavailable source is explicit, not an empty file.

The model projection for a submitted review is frozen to the referenced revision. For a later revision, include the new comment state and a short indication of edits/removals; context compaction may summarize older review turns, but must preserve the latest unresolved user instructions and the review reference IDs. The server controls token budgets for manifests, comments, and fetched source content. Treat file contents, quotes, and review text as untrusted data under the current instruction hierarchy.

## UX

### Composer and transcript

- Add a compact `+` menu beside the composer: `File`, `Directory`, `Review changes`, `Comment on message`. A workspace picker supports search and keyboard navigation; paste/drop of a path resolves to the same typed file/directory ref after Host validation. Do not paste paths into the textarea automatically.
- Render draft chips above the textarea with type, short path/title, availability, and remove action. Clicking a chip opens a preview/details panel. The send button works with text, artifacts, or both. The same chips appear in queued items and sent user turns; queued edits open the full text-and-artifact draft.
- A sent review card shows its source turn(s), number of files/messages and comments, and a `View review` action. File/directory chips open the appropriate workspace view. No preview loads a whole directory by default. Keyboard focus, labels, and non-color status text are required.

### Reviewing prior file changes

- Add `Review changes` to the existing `WorkspaceChanges` card of each completed assistant turn. It opens a side panel, keeping the chat visible. The panel starts from that turn's saved net changes, with file list, saved diff, and comment editor. The user may add other earlier change cards from the same active branch.
- Selecting a diff line/range offers `Add comment`. Show the side (`old`/`new`), line numbers, source turn, and saved path. A file-row comment is available for binary files or missing diffs. A banner says `Historical changes from this turn`; if the current file differs, offer `Open current file` separately.
- The panel has `Save draft`, `Add to message`, and `Cancel`. `Add to message` places one review chip in the composer; it does not send the turn. The panel shows unsent comments and supports edit/delete inline. On submit, the review is frozen as a revision.

### Reviewing earlier answers and questions

- Selecting text in one earlier user or assistant message offers `Comment on selection`. The mini editor shows the exact quote and source author/turn. For mouse, touch, and keyboard selection, keep the selected range while the editor opens. The message's `...` menu also offers `Comment on whole message`.
- Existing comments appear as unobtrusive markers on the source message and in the review panel. Clicking a marker scrolls to the quote and opens the comment; multiple comments on one range are grouped. Failed anchor verification shows the saved quote with `Selection no longer matches` and a reattach action.
- The comment list supports `Edit` and `Remove` for drafts. Sent comments offer `Revise review`, which prepares a new revision and makes clear that the update is sent as a new turn. A user can back out without altering the submitted record.

### Example flow

```text
Assistant turn: 3 files changed              [Review changes]
  Home.razor  +21 -4
    new line 344 -> Add comment -> "Handle the empty state."
Assistant answer: "All cases are covered."
  select "All cases" -> Comment -> "Please check the queued case."
Composer: [Review · 2 comments] [Home.razor]  Please fix these.
                                                  [Send]
```

## Validation and phased delivery

The Host enforces same-project workspace paths, allowed-root containment including symlink resolution, same-chat review sources, branch ancestry at submit, bounded counts/sizes, and current read permissions when resolving live files. A path ref is neither a permission grant nor proof that the file exists forever. Avoid rendering arbitrary file content as trusted HTML. Review source links use IDs and typed routes; never accept a free-form `file://` URL as an artifact target.

Suggested increments:

1. Structured file/directory refs through draft, submit, queue, persistence, transcript, and bounded model manifest. Artifact-only sending is part of this increment.
2. Review of saved `WorkspaceChanges`, including file-level and line comments, historical-source resolver, and review revisions.
3. Whole-message and arbitrary text-selection comments, with anchor verification and reattachment UX.

The resource service, shared envelope, and model projection are designed once; each increment adds only a target/resolver and its UI. Verify round trips across queue/retry/fork/replace, resource create/retire, source retention, permission failures, stale files, missing diffs, renderer changes, and model requests that contain refs without embedded file bodies.

## Open product choices

- Whether a saved review draft should sync across devices or remain local until submitted. The initial recommendation is local per chat/branch, matching the current composer draft.
- Whether users need comments on tool outputs. The initial scope is user/assistant messages and saved workspace changes; tool outputs may be added as another source type later.
- Whether a revision should show a full comment history in the UI. The initial recommendation is to show the latest revision prominently with a link to earlier submitted turns.
