# Mermaid skills

The built-in `mermaid` playbooks create and maintain diagrams in chat or requested .mmd/Markdown
files. They preserve unrelated work and product behavior. Each skill has its own SVG icon.

## Choosing a workflow

| Need | Skill |
| --- | --- |
| Choose a diagram type or use another supported Mermaid type | `mermaid-create` |
| Algorithm, decisions or process | `mermaid-create-flowchart` |
| Calls and responses in a scenario | `mermaid-create-sequence` |
| Types, members and static relationships | `mermaid-create-class` |
| Lifecycle and transitions | `mermaid-create-state` |
| Schema, keys and cardinalities | `mermaid-create-er` |
| Services/resources and boundaries | `mermaid-create-architecture` |
| Dates, durations and dependencies | `mermaid-create-gantt` |
| Concepts and hierarchy | `mermaid-create-mindmap` |
| Actual or illustrative branching workflow | `mermaid-create-gitgraph` |
| Requested diagram changes | `mermaid-edit` |
| Syntax/rendering failure | `mermaid-fix` |
| Read-only semantic, visual and accessibility review | `mermaid-review` |
| Actual SVG, PNG or PDF artifact | `mermaid-export` |

## Sources and destinations

The request may supply requirements, Mermaid source, paths and a target renderer or output.
Explicit values bypass questions. Ambiguous scope uses the file/directory pickers in `ask_user`;
actual GitGraph sources may use single/multiple branch or commit selection. Other essential
choices use ordinary options. Selection never grants filesystem or external-service access.

Creation returns a complete `mermaid` fenced block by default. Saving/editing happens when the
request or current documentation task authorizes it. Existing IDs, relationships and unrelated
prose are preserved where possible. Proposed designs and synthetic history/schedules are labeled,
and actual behavior is traced to inspected code/configuration. GitGraph operations inside diagram
source are syntax, not commands to mutate a repository.

## Layout for chat

Prefer narrow diagrams that read from top to bottom because the chat viewport is vertical.
An explicitly requested orientation or destination requirement takes precedence. Keep labels
concise and split wide diagrams into focused views while preserving relevant relationships;
do not turn alternatives, concurrent behavior or dependencies into an invented sequence.

Keep diagrams small. Oversized diagrams are hard to read in the chat, so prefer a few focused
views over one complete view; do not shrink labels or dimensions to force an oversized diagram
into one image. The same guidance applies during creation, requested layout changes, fixes,
exports and reviews.

- Flowcharts use `flowchart TD` or `flowchart TB`, including vertical subgraphs where practical.
  Connections outside a subgraph can override its direction; check the rendered result.
- Class and state diagrams use `direction TB`; ER diagrams use it when the target supports it.
- Architecture views prefer vertical service layers with connections such as `api:B --> T:db`.
  The flowchart fallback uses `flowchart TB`; `architecture-beta` has no flowchart direction directive.
- GitGraph uses `gitGraph TB:` on supporting runtimes (Mermaid 10.3+). Branches still occupy width.
- Sequence diagrams already advance downward in time; use concise participant aliases/messages
  and focused scenarios to reduce width. Gantt keeps its horizontal time axis; use focused time
  windows or sections. Mindmap keeps its radial layout; offer a `flowchart TB` hierarchy when
  vertical presentation is needed. Do not invent direction directives for these types.

Apply the preference during creation and requested layout changes. Content-only edits,
syntax-only fixes and exports preserve source orientation. Reviews recommend improvements
without editing. For visual verification, check the actual chat width, horizontal overflow
and label readability as well as the diagram's overall layout.

## Compatibility and verification

Inspect the target's actual version and build before relying on current upstream features.
An architecture diagram uses `architecture-beta` only on a supporting runtime; a labeled
flowchart view provides an alternative. Mindmap, layouts, icons and host configuration also
depend on the build. Do not silently upgrade dependencies or weaken security to fix a diagram.

Use the project's available parser/runner or discover permitted rendering tools. Await
`mermaid.parse(source)` to check syntax; with suppressed errors, a false result is invalid.
Parser success does not verify layout or meaning. Render on the compatible runtime, then
inspect clipping, connectors, density and readability when visual tools are available.
Report these as separate checks; missing tools/access are unverified rather than successful.

Use supported `accTitle` and `accDescr` metadata and adjacent prose. Do not rely on color alone.
Preserve host security settings and keep private diagrams out of external editors/services.
For export, prefer an existing CLI, pass paths/arguments separately, verify the output's actual
format and inspect it. Keep editable source. A failed command or error placeholder is no export.

## Official references

These references were checked when the skills were added (2026-10-01); consult documentation
compatible with the selected runtime rather than assuming upstream defaults apply locally.

- [Diagram syntax and configuration](https://mermaid.js.org/intro/syntax-reference.html)
- [Flowchart](https://mermaid.js.org/syntax/flowchart.html)
- [Sequence](https://mermaid.js.org/syntax/sequenceDiagram.html)
- [Class](https://mermaid.js.org/syntax/classDiagram.html)
- [State](https://mermaid.js.org/syntax/stateDiagram.html)
- [Entity relationship](https://mermaid.js.org/syntax/entityRelationshipDiagram.html)
- [Architecture](https://mermaid.js.org/syntax/architecture.html)
- [Gantt](https://mermaid.js.org/syntax/gantt.html)
- [Mindmap](https://mermaid.js.org/syntax/mindmap.html)
- [GitGraph](https://mermaid.js.org/syntax/gitgraph.html)
- [API usage, parsing and rendering](https://mermaid.js.org/config/usage.html)
- [Accessibility](https://mermaid.js.org/config/accessibility.html)
- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli)
