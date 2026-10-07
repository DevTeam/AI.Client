---
id: skill-create
name: Skill create
icon: sparkles
kind: playbook
description: Create a User or Project skill from the user's description: interview, draft SKILL.md and save it after review.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"What the skill should do, in the user's words"},"scope":{"type":"string","enum":["User","Project"],"description":"Only when the user said"}},"additionalProperties":false}
tools: ["app_read","ask_user","app_skills","skill_search"]
---

Skill conventions:
- `id` is `<domain>-<action>[-<object>]` in lowercase kebab case. Domains: chat, project, memory,
  skill, instructions, code, git, devops, qa, mermaid, svg, settings, team; start a new domain only for a new area such as `doc`. The action
  is a verb: create, rename, compact, fork, add, remove, review, save, edit, implement, fix, run.
- `name` is the id in words with the first letter capitalized: `project-directory-add` becomes
  "Project directory add". An abbreviation keeps its official spelling: `qa-plan` becomes "QA plan",
  `svg-create` becomes "SVG create", `devops-ci-fix` becomes "DevOps CI fix", `settings-import-mcp`
  becomes "Settings import MCP", `code-run-csharp` becomes "Code run C#".
- `icon` names the picture in the `/` list, chosen for what the skill does. One of: skill, sparkles,
  wand, zap, lightbulb, target, rocket, play, tool, code, terminal, bug, flask, shield, lock,
  search, eye, diff, git-branch, fork, message-circle, minimize, list-checks, edit, file, file-text,
  folder, folder-plus, folder-minus, project, package, database, memory, eraser, scroll, book, tag,
  link, globe, languages, mail, users, calendar, clock, chart, image, archive, trash, download,
  import, export, refresh, settings, skill-import, settings-import-mcp, cpu, circuit-board, server, network, cloud, cloud-upload,
  hard-drive, monitor, smartphone, wifi, workflow, git-merge, git-commit, brackets, regex, binary,
  variable, puzzle, blocks, filter, clipboard, clipboard-check, file-code, file-search, notebook,
  graduation-cap, bookmark, library, table, calculator, chart-line, chart-pie, trending-up, gauge,
  activity, timer, alarm, calendar-check, repeat, history, palette, brush, pen-tool, crop, camera,
  video, microphone, headphones, music, map, git-rebase, git-rebase-auto, git-merge-auto, code-plan,
  code-explain, code-refactor, code-tests-add, code-build-fix, code-review, code-performance-optimize,
  code-dependencies-update, code-security-review, code-docs-update, code-run-csharp, code-run-shell, devops-ci-create, devops-ci-fix,
  devops-container-create, devops-compose-configure, devops-config-review, devops-release-prepare,
  devops-deploy, devops-rollback, devops-incident-diagnose, devops-observability-configure,
  devops-infrastructure-change, devops-backup-verify, qa-plan, qa-acceptance-define, qa-cases-create,
  qa-exploratory-test, qa-ui-test, qa-api-test, qa-e2e-create, qa-regression-run,
  qa-test-data-prepare, qa-accessibility-review, qa-compatibility-test, qa-bug-report,
  qa-failures-triage, qa-release-assess, mermaid-create, mermaid-create-flowchart,
  mermaid-create-sequence, mermaid-create-class, mermaid-create-state, mermaid-create-er,
  mermaid-create-architecture, mermaid-create-gantt, mermaid-create-mindmap, mermaid-create-gitgraph,
  mermaid-edit, mermaid-fix, mermaid-review, mermaid-export, svg-create, svg-create-icon,
  svg-create-illustration, svg-create-schematic, svg-create-diagram, svg-create-chart,
  svg-create-infographic, svg-create-logo, svg-create-pattern, svg-create-animation,
  svg-create-sprite, svg-edit, svg-fix, svg-optimize, svg-review, svg-export, stopwatch,
  chat-schedule-create, chat-schedule-edit, chat-schedule-delete, chat-schedule-run.
  Only when the user asks for a picture none of these give,
  `icon` is SVG path data instead: one line starting with `M`, drawn as a 2px stroke on a 24x24
  grid, such as `M12 3 3 8l9 5 9-5ZM3 13l9 5 9-5`.
- `description` is one sentence that starts with a verb, leads with the task the skill is for (the
  model picks skills from the catalog by it) and names every side effect ("… after confirmation",
  "changes nothing").
- `kind: generic` turns its parameters into JSON in an isolated model without tools and needs a
  `result` schema. `kind: playbook` is followed by the calling model with its ordinary tools,
  lists them in `tools` and has no `result`. Prefer a playbook whenever the skill reads or
  changes application data or files.
- `parameters` is a one-line JSON Schema object with `additionalProperties` false; keep optional
  everything the playbook can ask for.
  Put input fields inside `properties`, not at the schema root, for example:
  `parameters: {"type":"object","properties":{"path":{"type":"string"}},"additionalProperties":false}`.
- A playbook body is numbered steps. Every change is confirmed with `ask_user` unless the exact
  value came from the user; the recommended option comes first with " (Recommended)"; say what
  dismissed, expired and interrupted answers do. A playbook whose result is something to read (a
  report, a list, a draft) says in its last step that the final answer contains that result in
  full; one that only changes something finishes with a one-line report without ids or revisions.
  Never write "finish with one line" after a step that renders output: models then answer with the
  line and drop the output.
- Write the body in English. Quoted labels in it are examples: the calling model writes questions,
  options and answers in the user's language.

1. List existing skills with `skill_search`; if one already covers `goal`, offer `skill-edit`.
2. Ask in one `ask_user` call only for what is still missing: the scope (User for every project,
   Project for this one), the inputs, and whether it may change data.
3. Draft the full SKILL.md: frontmatter lines `id`, `name`, `icon`, `kind`, `description`,
   `parameters`, then `tools` or `result`, then `---` and the body.
4. Show the draft in your answer and call `ask_user` labelled "Save" with "Save (Recommended)" and
   "Change something"; a typed answer is the change to make, after which you ask again. Dismissed
   saves; expired or interrupted leaves the draft unsaved in your answer.
5. `app_skills` Save with the content, the scope, revision 0 and enabled true. On a validation
   error, fix the draft and save again.
6. Answer with one line: the id, and that it is now in the `/` list.

## Links to skills and tools

When naming a skill in visible answers, link its name with
[Skill name](aiclient://navigate/settings.skills?skillId=EXACT_SKILL_ID).
When naming a tool, use
[Tool name](aiclient://navigate/settings.tools?toolName=EXACT_TOOL_CALL_NAME).
Use real skill ids and full tool call names including the MCP server prefix from the catalog;
URL-encode query values. Skill links use the effective current-project catalog. For another
project append &projectId=REAL_PROJECT_ID. Link saved skills in the completion report too.
The app supplies icons; do not add emoji or Markdown attributes. Links open settings or the
skill document; clicking them never executes a skill/tool or changes permissions.
