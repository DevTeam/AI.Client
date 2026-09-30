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
line. Optional `aliases` is a JSON array of up to 8 short commands spelled like an id, for example
`aliases: ["compact"]`: typing one in full after `/` puts the skill first in the list. A row found
by an alias (in full, by its start, or by its letters in order, so `/coma` still finds it) leads with
the best matching `/alias`, highlighted, and names the skill after it; the chip keeps the name. Also,
`app_skill_search` and `app_read resource=Skills` find the skill by it. An alias never replaces the id,
which chips and `app_run_skill` still use. `kind` selects how the skill runs:

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
  `project`, `memory`, `skill`, `instructions`, `code`, `git` and `settings`; a new area gets a new domain. The
  action is a verb: create, rename, compact, fork, add, remove, review, save, edit, suggest,
  implement, fix, run, commit.
- `name` is the id in words with the first letter capitalized (`project-directory-add` →
  "Project directory add"), so the `/` list groups skills by domain.
- `description` is one sentence that starts with a verb, leads with the task the skill is for and
  names every side effect. The model chooses from the catalog by it.
- Playbooks confirm every change with `ask_user` unless the exact value came from the user, put
  the recommended option first with " (Recommended)", say what a dismissed, expired or
  interrupted question does, and end with a one-line report without ids or revisions.

`BuiltInSkillCatalogTests` checks the naming of every bundled skill.

## Built-in skills

| id | What it does | Tools |
|---|---|---|
| `chat-rename` | Names a new chat after its first answer, or renames one on request (executor) | — |
| `chat-reply-suggest` | Drafts the user's next message to the last answer of a branch for the composer (executor) | — |
| `skill-route` | Picks the skills and first tools for the message that starts a turn (executor); Settings → Chat turns it off | — |
| `chat-compact` | Summarizes the chat and continues in a new chat that starts from the summary, and opens it | `app_chats`, `app_runs`, `app_navigate` |
| `chat-context-compact` (`/compact`) | Replaces the finished work of the current turn with a model-only summary, or undoes it | `context_compact` |
| `chat-fork` | Starts a branch from an earlier user message with a new prompt, and opens it | `app_runs` Fork, `app_chats` RenameBranch, `app_navigate` |
| `chat-summary` | Summarizes this or another chat; read-only, the safe example for testing skills | `app_read` |
| `chat-branch-cleanup` | Deletes branches the user picks; never the main or current branch | `app_chats` DeleteBranch |
| `project-create` | Creates a project from picked directories with a suggested name and access, creates its first chat, submits the requested work there and opens it | `app_projects`, `app_security`, `app_chats`, `app_runs`, `app_navigate` |
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
| `code-feature-implement` | Implements a change end to end: baseline `git status`, explore, plan, edit, build and test, remove leftovers, summary; never commits | file tools, `process_run` |
| `code-bug-fix` | Reproduces a bug, finds the root cause, adds a failing test, fixes it and reruns the tests | file tools, `process_run` |
| `code-tests-run` | Finds the project's test command, runs it and reports each failure; offers `code-bug-fix` | `process_run`, `run_skill` |
| `code-changes-review` | Reviews uncommitted changes for stray files, secrets, debug leftovers and unrelated edits; cleans up what the user picks | `process_run`, `edit_file`, `delete_file` |
| `git-commit` | On request only: drafts a message in the log's style and commits the chosen paths; never pushes | `process_run` |
| `settings-add-connections` | Discovers models from API URLs and adds or merges Connections by URL + model, preserving credentials and unrelated settings; offers comparison | `app_read`, `app_security`, `ask_user`, `run_skill` |
| `settings-review-connections` | Compares selected Connections with bounded synthetic tasks, reports quality, measured run time and errors, then applies approved defaults, subtask pools or cleanup | `app_read`, `spawn_subtask`, `app_security`, `app_projects`, `app_chats`, `ask_user` |
| `settings-select-connection` | Selects an enabled connection for the global default, current project/chat or subtask pool; can restore project/chat inheritance | `app_read`, `app_security`, `app_projects`, `app_chats`, `ask_user` |

Connection discovery is available through `app_read resource=ConnectionModels`: `query` is a new
OpenAI-compatible Base URL (without authentication); `resourceId` alone selects a saved connection
and uses its URL and stored credential on the Host. Combining an id and a URL is rejected, so a
stored key cannot be redirected. The result is the ordinary paged read result with model ids,
display names and owner tags. Discovery uses the same resolver and 15-second ceiling as the editor;
it does not create a Connection or prove completion support. Keys needed by new Connections are
entered in Settings → Connections; the playbooks do not ask for secrets in chat.

The comparison uses explicit connection ids in `spawn_subtask`, at most eight tasks per batch and
two small suites per candidate. Its `elapsedMilliseconds` measures the whole delegated run,
including application overhead and tool calls, on successes and failures. Price stays unknown
unless the user supplied it; stored Cost/Capability values are coarse user ratings, not prices or
benchmark scores. Default, subtask and cleanup changes are proposed for approval, and referenced
connections need a working replacement before deactivation or deletion.

The history of a chat is compacted for the model only while a request would not fit, and
`context_compact` only covers the finished work of the current turn; `chat-context-compact` runs it
on request and points to `chat-compact` when the turn has nothing to compact. `chat-compact` is the
explicit way to start over with a small context: the old chat stays unchanged. Running it is the request,
so it does not ask for confirmation.

`chat-rename` has a dedicated executor because it changes the chat title under the chat mutation
lock. Automatic naming starts after the first main-branch answer; explicit requests can rename
an existing chat. `chat_id=current` resolves from the trusted tool-run context, and a UUID is
looked up only inside that project. Manual renames prevent a late automatic result from applying.

`chat-reply-suggest` is an executor so that it can run after every answer without a turn of its
own: one model call without tools gets the answer and the user message before it, and returns one
short message in the user's language, or `NONE`. It changes nothing; `IChatReplySuggestions` keeps
the draft per branch for the answer it was written for (`message_id`), and the Web reads it with
`GET /api/projects/{p}/chats/{c}/reply-suggestion?branchId=&leafMessageId=`, which waits for a draft
in progress, or `POST` to the same URL, which writes one now. Settings → Chat switches the automatic
drafts and the automatic chat names off (`PUT /api/settings/chat-automation`); the explicit
requests still work. See [Composer rules](15-composer-rules.md#suggested-reply).

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

## How the model picks skills

When the App tools are available, the standing `skills.catalog` layer lists every enabled skill
in effect for the project: its id, description and parameter names, with `*` marking required ones.
Only executors the application runs on its own (`chat-reply-suggest`) are left out. It follows
memory, has its own 3,072-token budget and ends with a pointer to `app_skill_search` when it is
cut. Its lead tells the model to check the list before acting and when the user changes task, to
run a fitting skill before other tools even for requests that look simple, and to run only listed
or user-named ids. `run_skill` is always in the request's tool schema, so a skill from the catalog
runs without a `tool_search`.

A playbook's instructions stay in the conversation as the `app_run_skill` result. Before every model
step `ChatAgent` adds the run instruction `run.active-skill` naming the latest playbook loaded within
the last four user messages (`ISkillGuide.ActivePlaybookAsync`; a failed load does not count). It
tells the model to keep following that playbook while the user continues its task and to choose
again from the catalog when the task changes. A playbook can hand the work on by naming another
skill, as `code-tests-run` does with `code-bug-fix`.

`project-create` hands the work on instead of doing it: the chat that asked for the project belongs
to another project, so its file tools cannot reach the new directories. The playbook creates the
new project's first chat, submits the requested task there with `app_runs` and opens it with
`app_navigate`; the new chat's own turn is routed to `code-feature-implement` or another skill.

### Routing each turn

The catalog alone was not enough: a model read it and still did by hand what a playbook covers.
So before the first model step of every interactive turn that starts with a new user message,
`ChatAgent` asks `ISkillRouting`, which runs the `skill-route` executor: one model call without
tools on the chat's own connection, 12 seconds at most. It gets the message, the end of the
previous answer, the active playbook, the catalog lines and the permitted tools by their first
sentence, and returns up to two skill ids and eight tool names; unknown ones are dropped. The
tools, and those the chosen skills declare, are pinned before the tool selector cuts the list, so
the first step has them without `app_tool_search`.

When the first skill is a playbook without required parameters, `ChatAgent` loads it itself: it
persists an `app_run_skill` call with `parameters: {}` and its result, as if the model had made
them, and plans the first step again with them in the context. Loading a playbook only returns
its instructions, so it needs no approval, and the transcript shows it as an ordinary skill call.
A hint alone was not enough: told to call `app_run_skill` first, a model still asked its own
questions and did the work by hand, and a playbook's last steps (such as `project-create` opening
the project's first chat) never ran. The run instruction `run.skill-route` (until the model's
first answer) then says to follow the loaded instructions from step 1, taking parameter values
from the message. For a skill with required parameters it says to call `app_run_skill` first,
before any question. A second skill is for the rest of the request. When the router returns only
the active playbook, the instruction says the message continues it.

A message whose skill the user picked in the `/` list, and a turn that resumes after an approval,
are not routed. Routing that fails, times out or finds nothing leaves the turn as it was.
"Pick a skill for each new message" in Settings → Chat (`ChatAutomation.RouteSkills`) turns routing off.
