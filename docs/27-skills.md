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
`mcp_app__skill_search` and `app_read resource=Skills` find the skill by it. An alias never replaces the id,
which chips and `mcp_app__run_skill` still use. `kind` selects how the skill runs:

| kind | Runs in | Declares |
|---|---|---|
| `generic` (default) | an isolated model call without tools; the returned JSON is validated against `result` | optional `result`, no `tools` |
| `playbook` | the calling model's own turn, with its ordinary permission-checked tools | `tools` it uses, no `result` |
| `executor` | bundled code; only built-in skills | — |

A playbook run does no model call: `mcp_app__run_skill` validates the arguments and returns
`output.instructions` (the body after the frontmatter), the arguments, the declared tools and
`output.context` with the current project, chat and branch ids, which the model cannot see
otherwise. The model then follows the steps in the same turn. The run pins the declared `tools`
so they are in the next step's schema without a `tool_search`; it does not grant anything. A skill's text cannot grant directory access or other
application permissions, and every write still goes through tool approval.

## Naming

- `id` is `<domain>-<action>[-<object>]` in lowercase kebab case. The domains are `chat`,
  `project`, `memory`, `skill`, `instructions`, `code`, `git`, `devops`, `qa`, `mermaid`, `svg` and `settings`; a new area gets a new domain. The
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
| `chat-tool-risk-assess` | Judges one pending tool call for the chat's "Approve for me" mode (executor); see [security](06-security.md#chat-approval-mode) | — |
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
| `code-plan` | Researches a change and produces alternatives, affected files, implementation steps and acceptance checks; changes nothing | file readers, `process_run`, path picker |
| `code-explain` | Traces selected code, callers and data flow and explains behavior with source references; changes nothing | file readers, `process_run`, path picker |
| `code-refactor` | Improves structure while preserving behavior and public contracts, then verifies the change | file tools, `process_run`, path picker |
| `code-tests-add` | Adds meaningful behavior, boundary and regression tests using the existing framework and runs them | file tools, `process_run`, path picker |
| `code-build-fix` | Reproduces and fixes compiler, build and analyzer diagnostics, then follows residual errors | file tools, `process_run`, path picker |
| `code-review` | Reviews selected code, working-tree changes, branches or commits for correctness and compatibility; changes nothing | file readers, `process_run`, path/branch/commit pickers |
| `code-performance-optimize` | Measures a bottleneck, makes a focused optimization and compares equivalent before/after runs | file tools, `process_run`, path picker |
| `code-dependencies-update` | Updates chosen package versions and lockfiles, adapts APIs and verifies compatibility | file tools, `process_run`, path picker, version choices |
| `code-security-review` | Traces trust boundaries and reports supported vulnerabilities and mitigations; changes nothing | file readers, `process_run`, path picker |
| `code-docs-update` | Updates documentation and examples from inspected behavior and verifies applicable links/examples | file tools, `process_run`, path picker |
| `devops-ci-create` | Creates and validates CI workflows with the existing platform, checks, caches and artifacts; does not trigger remote jobs | file tools, `process_run`, `ask_user` |
| `devops-ci-fix` | Diagnoses failed CI jobs and repairs their demonstrated pipeline cause; remote reruns require explicit authorization | file tools, `process_run`, `ask_user` |
| `devops-container-create` | Creates a Dockerfile/ignore rules, builds a local image and verifies isolated startup; never pushes images | file tools, `process_run`, `ask_user` |
| `devops-compose-configure` | Configures and validates a local multi-service stack with networks, persistence, secrets references and health checks | file tools, `process_run`, `ask_user` |
| `devops-config-review` | Reviews effective environment configuration, precedence, missing values, contradictions and secret exposure; changes nothing | file readers, `process_run`, `ask_user` |
| `devops-release-prepare` | Prepares version/notes, packages and checksums with the existing release targets and verifies artifacts; never publishes | file tools, `process_run`, Git pickers |
| `devops-deploy` | Prepares a concrete rollout/recovery plan, applies an authorized deployment to an explicit target and verifies health | file tools, `process_run`, environment/service/artifact choices |
| `devops-rollback` | Selects an actual previous release, checks data/configuration compatibility, performs an authorized rollback and verifies recovery | file tools, `process_run`, release/environment choices |
| `devops-incident-diagnose` | Correlates bounded logs, metrics and recent changes, reports supported causes and a recovery plan; changes nothing | file readers, `process_run`, environment/service choices |
| `devops-observability-configure` | Configures and tests useful logs, metrics, tracing, health checks and alerts; remote settings/notifications need specific authorization | file tools, `process_run`, `ask_user` |
| `devops-infrastructure-change` | Edits and validates existing IaC, generates the actual plan/change set and applies only authorized reviewed changes | file tools, `process_run`, environment/workspace choices |
| `devops-backup-verify` | Checks backup metadata/integrity and verifies restoration at an explicitly isolated authorized destination; never overwrites live data | file tools, `process_run`, backup/destination choices |
| `qa-plan` | Creates a risk-based QA plan with scope, layers, dependencies, environments and exit criteria | file readers, `process_run`, `ask_user` |
| `qa-acceptance-define` | Defines observable acceptance criteria and identifies unresolved requirements | file readers, `ask_user` |
| `qa-cases-create` | Produces traceable positive, negative and boundary test cases with setup and cleanup | file readers, `ask_user` |
| `qa-exploratory-test` | Runs a bounded exploratory session and reports actual observations and reproducible defects | file readers, `process_run`, `tool_search`, `ask_user` |
| `qa-ui-test` | Checks rendered UI states and interactions through an available browser or test runner | file readers, `process_run`, `tool_search`, `ask_user` |
| `qa-api-test` | Checks actual API responses, authorization and effects against the contract | file readers, `process_run`, `tool_search`, `ask_user` |
| `qa-e2e-create` | Adds and runs stable integrated journey tests using the existing framework | file tools, `process_run`, `tool_search`, `ask_user` |
| `qa-regression-run` | Selects and runs regression checks based on actual changes and affected contracts | file readers, `process_run`, `tool_search`, Git pickers |
| `qa-test-data-prepare` | Creates deterministic synthetic fixtures and loads them only into an authorized isolated target | file tools, `process_run`, `tool_search`, `ask_user` |
| `qa-accessibility-review` | Reviews actual semantics, keyboard/focus behavior and accessibility findings | file readers, `process_run`, `tool_search`, `ask_user` |
| `qa-compatibility-test` | Executes the available supported platform matrix and records unavailable combinations | file readers, `process_run`, `tool_search`, platform choices |
| `qa-bug-report` | Writes a reproducible defect report with expected/actual behavior, evidence and impact; never submits an issue | file readers, `ask_user` |
| `qa-failures-triage` | Diagnoses failed or flaky tests using bounded reruns and evidence; changes nothing | file readers, `process_run`, `tool_search`, `ask_user` |
| `qa-release-assess` | Assesses a specific release candidate against actual gates, defects and remaining risks | file readers, `process_run`, Git pickers |
| `mermaid-create` | Create a Mermaid diagram from the user's explanation or inspected sources, selecting a compatible type and returning source or requested documentation edits. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-flowchart` | Create a Mermaid flowchart of an inspected algorithm, process or decision tree with labeled outcomes and compatible syntax. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-sequence` | Create a Mermaid sequence diagram of an actual interaction with ordered participants, messages, alternatives and error paths. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-class` | Create a Mermaid class diagram from actual type contracts with relevant members, inheritance and supported relationship notation. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-state` | Create a Mermaid state diagram of a lifecycle with verified transitions, events, initial states and terminal outcomes. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-er` | Create a Mermaid entity relationship diagram from schemas or models with verified attributes, keys and relationship cardinalities. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-architecture` | Create a Mermaid architecture view with verified service boundaries and connections, using target-supported syntax or a clear flowchart fallback. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-gantt` | Create a Mermaid Gantt schedule from supplied dates, durations and dependencies without inventing commitments or completion status. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-mindmap` | Create a Mermaid mindmap from concepts or an inspected outline with a clear hierarchy, consistent indentation and supported styling. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-create-gitgraph` | Create a Mermaid GitGraph of an actual or proposed branching workflow without changing repository history or fabricating commit ancestry. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-edit` | Edit an existing Mermaid diagram for requested content or layout changes while preserving unrelated documentation and meaning. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-fix` | Fix a Mermaid syntax or rendering failure using the actual source, diagnostic and target runtime while preserving the intended diagram. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-review` | Review a Mermaid diagram for syntax, source fidelity, readability, accessibility and target compatibility without editing project files. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `mermaid-export` | Export a Mermaid source to requested SVG, PNG or PDF using an available compatible renderer, preserving source and verifying the actual artifact. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create` | Create an editable SVG image from the user's description, choosing suitable vector geometry and delivering a complete preview or requested files. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-icon` | Draw original SVG icons with consistent geometry, optical alignment and readable details at the intended small sizes. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-illustration` | Draw an original SVG illustration or vector painting with deliberate composition, layered shapes, color and editable detail. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-schematic` | Draw an SVG schematic with verified components, labeled connections and precise layout without inventing system or engineering facts. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-diagram` | Draw a precise SVG diagram of relationships, flows or structure with source-based meaning, readable labels and clear connectors. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-chart` | Draw an SVG data chart from supplied values with honest scales, units, labels and a readable visual encoding. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-infographic` | Draw an SVG infographic with a clear information hierarchy, accurate supplied facts and readable typography. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-logo` | Draw an original SVG logo or wordmark with a recognizable silhouette, coherent lettering and usable monochrome variants. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-pattern` | Draw a reusable SVG pattern or ornament with deliberate tile geometry, palette and verified seamless repetition. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-animation` | Create a script-free SVG animation with a usable static fallback and target-aware motion verification. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-create-sprite` | Create an SVG symbol sprite from requested icons with stable unique IDs, preserved geometry and a complete usage preview. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-edit` | Edit an existing SVG image for requested visual changes while preserving unrelated geometry, references and consumers. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-fix` | Fix an SVG parsing, geometry or rendering defect using the actual source and target while preserving intended artwork. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-optimize` | Optimize SVG source size or rendering cost with measured before-and-after results while preserving appearance and consumer contracts. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-review` | Review SVG source and rendered output for correctness, readability, accessibility, portability and unnecessary complexity without editing files. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `svg-export` | Export an SVG image to requested PNG, WebP, JPEG or PDF with an available renderer, explicit dimensions/background and verified output. | file tools/readers, `process_run`, `fetch`, `tool_search`, `ask_user` |
| `git-commit` | On request only: drafts a message in the log's style and commits the chosen paths; never pushes | `process_run` |
| `git-commit-message-suggest` | Drafts a message for staged/local changes or selected commits; changes nothing | `process_run`, `ask_user` commit picker |
| `git-rebase` | Rebases the current branch onto a selected branch, keeps a recovery ref and pauses at conflicts | `process_run`, `ask_user` branch picker |
| `git-merge` | Merges a selected branch into the current branch, keeps a recovery ref and pauses at conflicts | `process_run`, `ask_user` branch picker |
| `git-rebase-auto` | Rebases and resolves compatible conflicts, checking each continuation; asks when intent is ambiguous | branch picker, file tools, `process_run` |
| `git-merge-auto` | Merges and resolves compatible conflicts, then checks the result; asks when intent is ambiguous | branch picker, file tools, `process_run` |
| `git-conflicts-resolve` | Resolves an existing operation's conflicts and continues authorized work after checks; also handles stash conflicts | file tools, `process_run`, `ask_user` |
| `git-history-review` | Reviews selected branches or commits and their diffs; changes nothing | branch/commit pickers, `process_run` |
| `git-cherry-pick` | Applies one or several selected commits in agreed order; keeps a recovery ref and pauses at conflicts | commit picker, `process_run` |
| `git-revert` | Undoes selected commits with new commits in agreed order; keeps a recovery ref and pauses at conflicts | commit picker, `process_run` |
| `git-stash` | Saves local work or applies a selected stash, retaining the stash until restoration is verified | `process_run`, `ask_user` |
| `settings-add-connections` | Discovers models from API URLs and adds or merges Connections by URL + model, preserving credentials and unrelated settings; offers comparison | `app_read`, `app_security`, `ask_user`, `run_skill` |
| `settings-review-connections` | Compares selected Connections with bounded synthetic tasks, reports quality, measured run time and errors, then applies approved defaults, subtask pools or cleanup | `app_read`, `spawn_subtask`, `app_security`, `app_projects`, `app_chats`, `ask_user` |
| `settings-select-connection` | Selects an enabled connection for the global default, current project/chat or subtask pool; can restore project/chat inheritance | `app_read`, `app_security`, `app_projects`, `app_chats`, `ask_user` |

Code skills take their scope from the request or optional `paths`; only ambiguous scope opens
the file/directory picker. A picked file returns in `other`, several directories in `paths`;
selection never grants access. All ten specialized code skills have their own SVG icon in the
shared picker. Their optional inputs also allow automatic playbook loading by the turn router.
The router prefers a specific code workflow and switches from a read-only plan/review to the
appropriate implementation skill when the user asks to carry it out.

Planning, explanation, correctness review and security review produce complete reports without
editing source. The mutating skills preserve existing work, perform the requested changes without
a redundant blanket confirmation, and clarify only new scope or consequential decisions. They
do not commit, push, deploy or run database migrations. Refactoring preserves behavior; test
creation checks observable contracts; build repair addresses causes rather than suppressing
diagnostics; performance claims require comparable measurements; dependency versions are checked
against the configured registry and official migration notes; documentation follows actual code.

DevOps skills use the configured tools/providers and existing runbooks; no connector, CLI or
credentials are assumed to be available. Scope uses the file/directory picker when ambiguous;
environments, services and immutable artifact versions use ordinary `ask_user` options discovered
from real configuration or permitted queries, with at most eight options per question. Release
source branches/commits use the Git pickers. Exact choices already supplied bypass questions;
an unanswered required target never defaults to production. Every DevOps skill has a dedicated
SVG icon in the shared picker, and `devops` is a skill domain.

Config review and incident diagnosis are read-only. Local CI/container/stack/release preparation
produces verifiable files/artifacts without implicitly publishing or deploying them. Operational
changes first prepare the concrete account/context, target, artifact/resources, commands, checks
and recovery; missing authorization is requested after preparation, and exact existing
authorization is not requested twice. Rollback checks schema/data compatibility instead of
assuming a binary rollback reverses migrations. IaC uses the actual reviewed plan/change set and
checks for drift before applying. Backup restoration is tested only in an explicitly isolated
destination. Observability tests do not notify real recipients without authorization. Results
distinguish configuration preparation, submitted operations and verified completion.

QA skills use file/directory pickers for ambiguous scope and Git branch/commit pickers for an
unspecified source revision; supplied values bypass selection. Environments, accounts, scenarios
and platforms use ordinary `ask_user` choices from available evidence. They discover browser/API
tools through `tool_search` or use the project's existing runner; missing access/tools block the
affected check. Source inspection alone never proves rendered behavior or a passing runtime check.
Every QA skill has its own SVG icon in the shared picker, and `qa` is a skill domain.

Planning, criteria, cases, bug reports and release assessments produce chat reports. E2E creation
and test-data preparation may edit the requested tests/fixtures while preserving product behavior.
Execution uses exact authorized environments and disposable synthetic data; live deletion,
charges or messages to real recipients require the corresponding authorization. Cleanup touches
only task-owned records. Results identify the actual revision/environment and distinguish passed,
failed, blocked and not-run checks. No discovered tests, an unavailable platform or a pending run
is not a pass; a passing retry does not erase a flaky failure. Release readiness assesses evidence
and risks without implicitly publishing, deploying or waiving a quality gate.

Mermaid skills return a complete fenced diagram in chat by default, editing documentation/source
files only within the requested scope. Each has a dedicated SVG icon in the shared picker.
They inspect the target version/build and fetch official syntax documentation; current upstream
features are not assumed to exist in the host. General creation covers types beyond the specialized
flowchart, sequence, class, state, ER, architecture, Gantt, mindmap and GitGraph workflows.
Editing preserves unrelated content, repair preserves meaning, review is read-only, and export
uses an actual available compatible renderer without implicitly installing tooling.

Validation distinguishes parsing, rendering and visual inspection. Missing tools leave checks
unverified. Diagrams preserve host security settings, use accessible descriptions where supported,
and do not upload private source to an external editor. GitGraph is a visualization workflow:
branch/commit selection uses the existing Git pickers without executing history operations.
See the [Mermaid skill guide](mermaid-skills.md) for official references and workflow details.

SVG skills draw original editable vector artwork and return a complete `svg` fenced document
in chat unless file delivery is requested or needed by the existing implementation task.
They cover icons, illustrations/paintings, schematics, precise diagrams, charts, infographics,
logos, patterns, script-free animation and symbol sprites, plus editing, repair, measured
optimization, read-only review and actual export. Each has a dedicated SVG icon in the picker.
Files/directories use the existing scope pickers; known values bypass questions.

Chat SVG is an image context that does not inherit application theme/currentColor. Artwork uses
explicit colors/backgrounds with a namespace, useful viewBox, valid XML and local references.
Skills distinguish XML validation, compatible rendering and visual inspection. They preserve
host restrictions, require no external resources, and report unavailable checks honestly.
Animation needs real motion/fallback verification; optimization preserves accessibility and public
IDs; export verifies actual format, dimensions and background. Review changes no project files.
See the [SVG skill guide](svg-skills.md) for workflow details and official references.

Git skills use the [Git pickers in ask_user](19-ask-user.md#git-pickers) when the user has not named
the branches or commits. Explicit values from the request bypass selection. History operations
check for local work and ongoing operations first, and do not silently stash, discard changes or
push. The automatic conflict variants inspect base/ours/theirs and preserve compatible intent;
they do not blanket-select a side or skip commits to make an operation pass. Ambiguous conflicts
leave the operation paused for a user decision. Every Git skill has an icon; rebase, automatic
rebase and automatic merge also have dedicated icons in the shared icon picker.

Connection discovery is available through `app_read resource=ConnectionModels`: `query` is a new
OpenAI-compatible Base URL (without authentication); `resourceId` alone selects a saved connection
and uses its URL and stored credential on the Host. Combining an id and a URL is rejected, so a
stored key cannot be redirected. The result is the ordinary paged read result with model ids,
display names and owner tags. Discovery uses the same resolver and 15-second ceiling as the editor;
it does not create a Connection or prove completion support. Keys needed by new Connections are
entered in Settings → Connections; the playbooks do not ask for secrets in chat.

The comparison uses explicit connection ids in `spawn_subtask`, at most eight tasks per batch and
two small suites per candidate. Its `elapsedMilliseconds` measures the whole delegated run,
including application overhead and tool calls, on successes and failures. Cost stays unknown
unless token prices or reported usage are available; stored Capability values are coarse user
ratings, not benchmark scores. Default, subtask and cleanup changes are proposed for approval, and referenced
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

`chat-comment-suggest` works the same way for a review comment being written: one model call
without tools gets the fragment, the message it is from or the file and diff around the lines, and
returns one short comment or `NONE`. It changes nothing and nothing keeps the draft;
`IReviewCommentSuggestions` runs it for `POST /api/projects/{p}/chats/{c}/reviews/comment-suggestion`
and skips an automatic request while Settings → Chat switches automatic comment drafts off. See
[Chat artifacts](24-chat-artifacts.md#suggested-comments).

## Tools

`mcp_app__skill_search` exposes enabled effective skills, their kinds and schemas to the main model. An
omitted query lists all enabled skills. If a query has no text matches, the result still lists
available skills with `matchedQuery=false` and guidance; an empty result means there really are
no enabled skills in the current scope. `mcp_app__run_skill` validates arguments and runs a skill in
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
Only executors the application runs on its own (`chat-reply-suggest`, `chat-tool-risk-assess`) are left out. It follows
memory, has its own 12,288-token budget and ends with a pointer to `mcp_app__skill_search` when it is
cut. Its lead tells the model to check the list before acting and when the user changes task, to
run a fitting skill before other tools even for requests that look simple, and to run only listed
or user-named ids. `skill_search` and `run_skill` are always in the request's tool schema, so a skill from the catalog
runs without a `tool_search`.

A playbook's instructions stay in the conversation as the `mcp_app__run_skill` result. Before every model
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
persists an `mcp_app__run_skill` call with `parameters: {}` and its result, as if the model had made
them, and plans the first step again with them in the context. Loading a playbook only returns
its instructions, so it needs no approval, and the transcript shows it as an ordinary skill call.
A hint alone was not enough: told to call `mcp_app__run_skill` first, a model still asked its own
questions and did the work by hand, and a playbook's last steps (such as `project-create` opening
the project's first chat) never ran. The run instruction `run.skill-route` (until the model's
first answer) then says to follow the loaded instructions from step 1, taking parameter values
from the message. For a skill with required parameters it says to call `mcp_app__run_skill` first,
before any question. A second skill is for the rest of the request. When the router returns only
the active playbook, the instruction says the message continues it.

A message whose skill the user picked in the `/` list, and a turn that resumes after an approval,
are not routed. Routing that fails, times out or finds nothing leaves the turn as it was.
"Pick a skill for each new message" in Settings → Chat (`ChatAutomation.RouteSkills`) turns routing off.
