# Skills

The Host embeds read-only built-in skills at `Skills/BuiltIn/<id>/SKILL.md`. Editable files live
under the Host data directory:

- `skills/user/<id>/SKILL.md` is available in every project.
- `skills/projects/<projectId>/<id>/SKILL.md` is available in that project.

When a user and project skill have the same ID, the project skill runs in that project. Built-in
IDs are reserved. Skills are edited like Memory: Settings → Skills shows the built-in and User
skills, and the project's popup menu opens Skills for that project's own. Both use the same
editor with the full SKILL.md; edits and removals stay local and are written when the drawer
closes, and a failed save keeps the drawer open with "Discard changes". Saving checks the
`revision` field and atomically replaces one file; a stale revision returns a conflict. Changing
the `id` saves a new skill and deletes the old one. Built-in skills stay read-only. Project skills
are deleted with their project.

Frontmatter requires `id`, `name`, `description` and a JSON Schema object on the `parameters`
line. `kind` selects how the skill runs:

| kind | Runs in | Declares |
|---|---|---|
| `generic` (default) | an isolated model call without tools; the returned JSON is validated against `result` | optional `result`, no `tools` |
| `playbook` | the calling model's own turn, with its ordinary permission-checked tools | `tools` it uses, no `result` |
| `executor` | bundled code; only built-in skills | — |

A playbook run does no model call: `app_run_skill` validates the arguments and returns
`output.instructions` (the body after the frontmatter), the arguments, the declared tools and
`output.context` with the current project, chat and branch ids, which the model cannot see
otherwise. The model then follows the steps in the same turn. The run pins the declared `tools`
so they are in the next step's schema without a `tool_search`; it does not grant anything. A skill's text cannot grant directory access or other
application permissions, and every write still goes through tool approval.

## Naming

- `id` is `<domain>-<action>[-<object>]` in lowercase kebab case. The domains are `chat`,
  `project`, `memory`, `skill` and `instructions`; a new area gets a new domain. The action is a
  verb: create, rename, compact, fork, add, remove, review, save, edit.
- `name` is the id in words with the first letter capitalized (`project-directory-add` →
  "Project directory add"), so the `/` list groups skills by domain.
- `description` is one sentence that starts with a verb and names every side effect.
- Playbooks confirm every change with `ask_user` unless the exact value came from the user, put
  the recommended option first with " (Recommended)", say what a dismissed, expired or
  interrupted question does, and end with a one-line report without ids or revisions.

`BuiltInSkillCatalogTests` checks the naming of every bundled skill.

## Built-in skills

| id | What it does | Tools |
|---|---|---|
| `chat-rename` | Names a new chat after its first answer, or renames one on request (executor) | — |
| `chat-compact` | Summarizes the chat and continues in a new chat that starts from the summary | `app_chats`, `app_runs` |
| `chat-context-compact` | Replaces the finished work of the current turn with a model-only summary, or undoes it | `context_compact` |
| `chat-fork` | Starts a branch from an earlier user message with a new prompt | `app_runs` Fork, `app_chats` RenameBranch |
| `chat-summary` | Summarizes this or another chat; read-only, the safe example for testing skills | `app_read` |
| `chat-branch-cleanup` | Deletes branches the user picks; never the main or current branch | `app_chats` DeleteBranch |
| `project-create` | Creates a project from picked directories with a suggested name and access | `app_projects`, `app_security` |
| `project-rename` | Offers three names that keep the current meaning and applies the chosen one | `app_projects` Update |
| `project-describe` | Drafts a description from the project's README and manifests | `app_projects` Update |
| `project-directory-add` | Grants more directories, read-only or read-write | `app_security` AddDirectoryGrant |
| `project-directory-remove` | Revokes grants the user picks | `app_security` RemoveDirectoryGrant |
| `project-security-review` | Reviews grants, servers and tool policies; changes nothing | `app_read` |
| `memory-save` | Saves a fact, updating a matching entry instead of duplicating it | `app_memory` |
| `memory-review` | Merges duplicates and drops stale entries the user approves | `app_memory` |
| `memory-forget` | Deletes matching entries after showing them | `app_memory` |
| `skill-create` | Interviews the user, drafts SKILL.md by these conventions and saves it | `app_skills` |
| `skill-edit` | Changes a User or Project skill after the user reviews the change | `app_skills` |
| `skill-from-chat` | Turns the workflow of the current chat into a playbook | `app_skills` |
| `instructions-edit` | Changes the project instructions with the smallest edit | `app_instructions` |

The history of a chat is compacted for the model only while a request would not fit, and
`context_compact` only covers the finished work of the current turn; `chat-context-compact` runs it
on request and points to `chat-compact` when the turn has nothing to compact. `chat-compact` is the
explicit way to start over with a small context: the old chat stays unchanged. Running it is the request,
so it does not ask for confirmation.

`chat-rename` has a dedicated executor because it changes the chat title under the chat mutation
lock. Automatic naming starts after the first main-branch answer; explicit requests can rename
an existing chat. `chat_id=current` resolves from the trusted tool-run context, and a UUID is
looked up only inside that project. Manual renames prevent a late automatic result from applying.

## Tools

`app_skill_search` exposes enabled effective skills, their kinds and schemas to the main model. An
omitted query lists all enabled skills. If a query has no text matches, the result still lists
available skills with `matchedQuery=false` and guidance; an empty result means there really are
no enabled skills in the current scope. `app_run_skill` validates arguments and runs a skill in
the current project. `app_read resource=Skills` lists the built-in, User and Project skills with
source, kind and revision, and returns the full SKILL.md when a query names the skill.
`app_skills` creates, updates, disables or deletes User and current Project skills with revision
checks and the ordinary tool-approval policy. `GET /api/skills` and the Skills drawer expose the
documents to the user. The last 50 run statuses and outputs are available through
`GET /api/skills/runs` and the drawer; this history is in memory until the Host restarts.

The user can invoke a skill from the composer: typing `/` lists the enabled skills, and the picked
one becomes a chip on the message (see [Composer rules](15-composer-rules.md#skills-from-the-slash-list)).
The message stores it as a `Skill` resource; submission rejects a skill that is missing or disabled
in the project, and the model's copy of the message starts with an instruction to run that skill.
