# Skills

The Host embeds read-only built-in skills at `Skills/BuiltIn/<id>/SKILL.md`. Editable files live
under the Host data directory:

- `skills/user/<id>/SKILL.md` is available in every project.
- `skills/projects/<projectId>/<id>/SKILL.md` is available in that project.

When a user and project skill have the same ID, the project skill runs in that project. Built-in
IDs are reserved. The Skills drawer uses Memory's two-column layout, groups skills by source,
and edits the full SKILL.md for User and Project scopes. Saving checks the `revision` field and
atomically replaces one file; a stale revision returns a conflict. Closing the drawer saves pending
edits. Built-in skills stay read-only. Project skills are deleted with their project.

Frontmatter requires `id`, `name`, `description` and a JSON Schema object on the `parameters`
line. Editable skills may also declare a JSON Schema `result`. The generic executor accepts
caller-supplied parameters, gives the model no application tools, validates the returned JSON
against `result`, and returns it to the caller. The main model prepares parameters with its
ordinary permission-checked tools. A skill's text cannot grant directory access or other
application permissions.

`app_skill_search` exposes enabled effective skills and their schemas to the main model. An omitted
query lists all enabled skills. If a query has no text matches, the result still lists available
skills with `matchedQuery=false` and guidance; an empty result means there really are no enabled
skills in the current scope. For a general request to test skill execution, the model can read
the current project with `app_read` and run the built-in `project-name` skill without changing data.
`app_run_skill` validates arguments and runs a skill in the current project. `app_skills` creates,
updates, disables or deletes User and current Project skills with revision checks and the ordinary
tool-approval policy. `GET /api/skills` and the Skills drawer expose the documents to the user.
The last 50 run statuses and outputs are available through `GET /api/skills/runs` and the drawer;
this history is in memory until the Host restarts.

`chat-title` has a dedicated executor because it changes the chat title under the chat mutation
lock. Automatic naming starts after the first main-branch answer; explicit requests can rename
an existing chat. `chat_id=current` resolves from the trusted tool-run context, and a UUID is
looked up only inside that project. Manual renames prevent a late automatic result from applying.

`project-name` is the first generic built-in skill. The main model reads the project with
`app_read` and passes its name and description to the skill; the skill returns a proposed name
as JSON. The main model can then use `app_projects` with the project's current revision when
the user requested the actual rename. This keeps the generic skill's model tools read-only while
project changes use the application's existing write and approval flow.
