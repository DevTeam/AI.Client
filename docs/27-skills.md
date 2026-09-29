# Skills

Status: first built-in skill implemented.

## First increment

The Host embeds `Skills/BuiltIn/<id>/SKILL.md` in its assembly. Frontmatter requires `id`, `name`,
`description`, and a JSON Schema object in `parameters`; the rest is sent as the skill's model instruction. `BuiltInSkillCatalog` reads
these files at startup and `GET /api/skills` returns their exact text for the Skills drawer.
Bundled skills are read-only. User and project skill storage, editing, and model-managed skills
belong to later increments.

The Skills drawer uses Memory's two-column layout: a list on the left and the selected skill's
description and `SKILL.md` on the right. Built-in skills have no edit or add controls.

The first skill, `chat-title`, starts in the background after the first main-branch answer to a
composer-created chat. Callers invoke it through `ISkillRunner` with a skill ID, JSON arguments,
and a trusted project scope. The runner validates arguments against the schema declared in SKILL.md.
`chat-title` requires `chat_id`; the same invocation shape can later be produced by composer commands
or a model-facing skill tool. Its model request initially contains only the skill and arguments. The model
must call `read_chat` to choose messages to inspect, then returns one title. That tool exposes
only bounded user and assistant text from the same chat. The run has a 20-second deadline and no
write tools. A failure keeps the provisional title and does not fail the chat run.

The chat document stores `AutoTitlePending`. Composer-created chats set it; explicit chat creation
does not. Manual rename clears it. The title skill rechecks it under the chat's mutation lock before
writing, so a late model result cannot replace a manual name. Existing documents without this field
default to false. A successful automatic rename announces an application data change so open
clients reload the sidebar.

## Next increments

- Add user and project skill directories under the Host data directory, with visible provenance.
- Add revision-checked skill management and approval for model-authored changes.
- Generalize the bounded runner and its tool set for project names, composer completion, UI help,
  topic compaction, and access configuration.
- Let the main chat search and activate skills when the relevant task arises.

Skills describe workflows. Tool policies and directory grants remain enforced by the application.
