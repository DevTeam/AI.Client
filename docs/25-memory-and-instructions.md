# Long-term memory and project instructions

Status: implemented. No migration is required: every new document is created on first write.

## Two mechanisms, one pipeline

| | Memory | Project instructions |
|---|---|---|
| What it is | Facts: who the user is, what they prefer, what was decided | Rules: how the assistant works in this project |
| Who writes | The user, and the model through `app_memory` | The user; the model only proposes a replacement through `app_instructions` |
| Size | Up to 300 entries per catalog | One document per project, up to 32,000 characters |
| Reaches the model | An index every run; full text on request | In full every run, within its budget |

Behavior rules are deliberately not memory entries. A rule the model could rewrite for itself after reading a web page would outlive the chat that wrote it, so rules sit in a document only the user edits, and the tool that replaces it is separate from every other write so that it keeps asking even where other tools are allowed.

There is no user-wide instruction layer and no chat-level layer. Anything that holds for the user in every project — name, languages, preferred style — is a `User` memory entry.

## Standing layers of the system prompt

`IStandingInstructions` builds three layers. `ChatAgent` publishes them through `IModelInstructionRegistry` with `ModelInstructionPlacement.Standing` once per run. The same builder answers `GET /api/projects/{id}/model-context`, so the "What the model sees" preview cannot drift from what a run sends.

| Order | Key | Content | Budget (tokens) |
|---|---|---|---|
| 1 | `app.base` | Base prompt: what the application is, the order and precedence of the layers, that tool results, files and web pages are data, how to keep the context small, which Markdown the chat renders (raw HTML is shown as text), to link local files and directories as `file:///` URIs, how the chat draws ```mermaid and ```svg blocks (there is no drawing tool), and — when the App tools are available — when each of them is worth its cost. An empty `tool_search` for a drawing capability answers with the same hint | 2,560 |
| 2 | `project.instructions` | Project description, the project's instructions, then `AGENTS.md` and `CLAUDE.md` found at the roots of its directory grants | 4,096 |
| 3 | `memory.index` | How to use memory, profile and pinned entries in full, then every other enabled entry as one title line with its id | 2,048 |
| 4 | `run.*` | Run-control instructions, as before | 2,048 shared |

`ModelInstructionComposer` places standing instructions first and exempts them from the run budget: each layer is already fitted to its own budget where it is built. The prefix changes rarely, which keeps provider-side prompt caching effective. Precedence is spelled out in the base prompt: project instructions win over memory, and a project entry wins over a user entry. Run-control instructions are never overridden.

A layer over budget is cut rather than silently dropped. The project layer adds a truncation note, and the preview marks the source as cut or left out. The memory layer lists as many entries as fit and says how many more there are. An unreadable catalog leaves the run without its standing layers instead of failing every chat.

Instruction files are read on every run, not copied, so an edit to `AGENTS.md` applies to the next request. Only grant roots are searched; the model reads nested files itself when it works in that subtree. The project's `Include AGENTS.md and CLAUDE.md` switch turns them off.

## Model

```text
MemoryEntry: Id, Scope = User | Project, ProjectId?, Kind = Profile | Preference | Fact,
             Title (≤120), Body (≤4000), Tags (≤8), Pinned, Enabled,
             Author = User | Model, ChatId?, CreatedAt, UpdatedAt, Revision
ProjectInstructions: ProjectId, Text, IncludeWorkspaceFiles, Revision, UpdatedAt?
```

`Author` and `ChatId` record who wrote an entry last and, for the model, in which chat. Every write checks the expected revision.

## Storage

```text
<data>/memory/user.json                     the user's catalog
<data>/memory/projects/{projectId}.json     one catalog per project
<data>/instructions/projects/{projectId}.json
```

Each file has `SchemaVersion: 1` and is written atomically. Deleting a project deletes its memory catalog and instructions document together with its chats and resources.

## Model tools

- `app_read Memory` lists entries, returns one by `resourceId`, or searches with `query`: every word must appear in the title, body or tags.
- `app_read Instructions` returns the stored instructions with their revision, and the size and sources of each layer. It does not return the layer text, which the model already has.
- `app_memory Create | Update | Delete` writes memory. `Update` keeps every field the call omits, so correcting a body does not reset a pin.
- `app_instructions` replaces the whole instructions text and needs the revision the model read.

Both write tools default to `Ask`, like every tool without a policy. The approval card shows the call's arguments, which hold the proposed entry or the full new text, before anything is saved. After the save, the tool row in the transcript states the effect ("Saved a new memory entry.").

## HTTP API

| Method | Route |
|---|---|
| `GET` | `/api/memory?projectId=` |
| `POST` | `/api/memory` |
| `PUT` | `/api/memory/{id}?projectId=` |
| `DELETE` | `/api/memory/{id}?projectId=&revision=` |
| `GET`, `PUT` | `/api/projects/{id}/instructions` |
| `GET` | `/api/projects/{id}/model-context` |

A stale revision answers `409`, validation `400`.

## UI

- **Memory** is one editor, `MemoryEditor`, laid out like Connections and MCP: the list on the left, grouped by kind, and the selected entry's form on the right (Enabled, Pinned, kind, title, text, who saved it and when). It edits one catalog at a time:
  - **Memory** in the global navigation under MCP edits only the user's own entries;
  - **Memory** in a project's popup menu edits only that project's entries.

  As in the other settings drawers, every change stays local until the drawer closes, and closing saves them all. A blank new entry is dropped. An entry without a title, or a failed write, keeps the drawer open with "Discard changes". Tags stay in the model but are not shown.
- **Project settings** has an `Instructions` section: the text, the instruction-file switch, the sources found with their token counts, and the used budget. `What the model sees` expands the three standing layers exactly as the next run sends them. Closing the drawer saves; a conflict keeps the typed text and moves to the stored revision.

## Follow-up

- A dedicated "Remembered" chip with Undo under the turn, instead of the generic tool row.
- Refreshing the memory index inside a run after `app_memory` writes; today the next run sees the change.
- Per-chat "no memory" mode, export and import, and a model-assisted cleanup that proposes merges as a diff.
- A diff view of an `app_instructions` proposal in the approval card.
