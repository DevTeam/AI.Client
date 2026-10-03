# Long-term memory and project instructions

Status: implemented. No migration is required: every new document is created on first write.

## Two mechanisms, one pipeline

| | Memory | Project instructions |
|---|---|---|
| What it is | Facts: who the user is, what they prefer, what was decided | Rules: how the assistant works in this project |
| Who writes | The user, and the model through `app_memory` | The user; the model only proposes a replacement through `app_instructions` |
| Size | Up to 300 entries per catalog | One document per project, up to 32,000 characters |
| Reaches the model | An adaptive index every run; full text on request | In full every run; an oversized request fails locally |

Behavior rules are deliberately not memory entries. A rule the model could rewrite for itself after reading a web page would outlive the chat that wrote it, so rules sit in a document only the user edits, and the tool that replaces it is separate from every other write so that it keeps asking even where other tools are allowed.

There is no user-wide instruction layer and no chat-level layer. Anything that holds for the user in every project — name, languages, preferred style — is a `User` memory entry.

## Standing layers of the system prompt

`IStandingInstructions` collects source layers and `IAdaptiveContextPolicy` selects their full or compact model-facing projection. `ChatAgent` publishes them through `IModelInstructionRegistry` with `ModelInstructionPlacement.Standing` once per run, passing the run's selected connection. The same builder answers `GET /api/projects/{id}/model-context` using the project/default connection. The preview identifies that scope because a chat can override it.

| Order | Key | Content | Budget (tokens) |
|---|---|---|---|
| 1 | `app.base` | Application boundaries, instruction precedence, rendering and tool-use guidance; authored full/compact variants | Shared adaptive instruction budget |
| 2 | `project.instructions` | Project description, user rules, then root `AGENTS.md` and `CLAUDE.md` | Retained in full |
| 3 | `memory.index` | Relevant facts and an index of saved entries, with a discovery pointer | Adaptive; at most 2,048 |
| 4 | `skills.catalog` | Skill ids and purposes; compact catalogs omit argument lists | Remaining standing share |
| 5 | `run.*` | Required completion protocol and optional step guidance | Adaptive; recommendation at most 2,048 |

`AdaptiveContextPolicy` owns all budgets and priorities; `ModelInstructionComposer` formats the result. The standing projection is chosen once per run, so growing history does not rewrite its prefix. Precedence is spelled out in the base prompt: project instructions win over memory, and a project entry wins over a user entry. Required run-control instructions are never omitted. Exact formulas are documented in [adaptive context selection](21-tool-selection-and-adaptive-compaction.md).

Optional indexes retain whole entries within their share and point to discovery for the rest. Project rules are never cut to the model's budget: the preview marks an exceeded recommendation, and a request that cannot fit fails with a size breakdown. The instruction-file reader retains its separate 64 KiB read limit and reports when it cut a file. An unreadable catalog leaves the run without its standing layers instead of failing every chat.

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
