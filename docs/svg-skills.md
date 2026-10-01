# SVG skills

These built-in playbooks draw editable vector artwork with native SVG geometry. They follow the
project's visual conventions, preserve unrelated work, and never implicitly publish or commit.
Every skill has an original icon in the shared picker.

## Workflows

| Need | Skill |
| --- | --- |
| General image brief | `svg-create` |
| Small readable icons | `svg-create-icon` |
| Illustrations and vector paintings | `svg-create-illustration` |
| Technical/conceptual schematics | `svg-create-schematic` |
| Precisely arranged relationships/flows | `svg-create-diagram` |
| Data charts with actual scales | `svg-create-chart` |
| Information layouts | `svg-create-infographic` |
| Original logo/wordmark | `svg-create-logo` |
| Repeated patterns/ornaments | `svg-create-pattern` |
| Script-free animation with static fallback | `svg-create-animation` |
| Reusable symbol sets | `svg-create-sprite` |
| Requested visual changes | `svg-edit` |
| Parse, reference or rendering defects | `svg-fix` |
| Measured size/cost reduction | `svg-optimize` |
| Read-only quality review | `svg-review` |
| PNG/WebP/JPEG/PDF conversion | `svg-export` |

## Delivery and scope

Creation returns a complete `svg` code fence by default; the chat displays it as an image.
Save assets only when requested or when required by the ongoing implementation task.
Explicit requirements and paths bypass questions. Ambiguous file/directory scope uses
`ask_user` pickers; essential style/output decisions use ordinary options. A picker grants no access.

The source has one SVG root with the SVG namespace, a useful viewBox, balanced XML,
unique IDs and working local references. Paint order and groups retain editable structure.
Diagrams/schematics follow inspected facts, charts use supplied data with honest scales,
and illustrative values are labeled. A vector illustration is not claimed to be a photograph.
Mermaid requests use the Mermaid skills instead of silently changing source formats.

## Rendering and verification

The chat's SVG preview uses an image URL, not DOM insertion. It does not inherit page CSS or
theme: use explicit colors appropriate for the requested background, or an intentional canvas.
Transparent output must remain transparent. Keep artwork self-contained: no scripts, event
handlers, DOCTYPE/entities, foreignObject or external fonts/images/stylesheets.

Parse XML safely with DTD/entity resolution disabled, then render with an available compatible
tool in the target embedding mode. Inspect size, clipping, readable labels and background.
XML parsing alone does not prove the image renders correctly. Report parsing, rendering and
visual inspection separately; missing tools/access leave checks unverified.

Reusable UI icons may follow currentColor conventions in their real inline consumers.
Inspect icons at the intended small sizes and theme variants. Internal title/desc metadata
is useful but does not replace appropriate host alt/accessible naming.

Patterns must show several repeats for seam checks. Sprites need visible use instances;
symbol definitions alone are not an image preview. Animation must be tested over time and
have a static/reduced-motion strategy suited to the host; a static rasterizer cannot verify it.
Optimizers preserve accessible metadata, meaningful IDs and consumer contracts, and report
measured bytes separately from speed claims. Export preserves editable source and verifies
real format/dimensions; JPEG requires an explicit opaque background.

## Official references

Checked when these skills were added (2026-10-01); validate actual target support rather than
assuming every SVG feature works in every browser, editor or export tool.

- [SVG specification](https://www.w3.org/TR/SVG2/)
- [SVG tutorial](https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorials/SVG_from_scratch/Getting_started)
- [viewBox](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Attribute/viewBox)
- [Patterns](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/pattern)
- [Symbols](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/symbol)
- [Animation and motion accessibility](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/animate)
