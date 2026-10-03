# ADR-009: One adaptive context policy

Date: 2026-10-03. Status: accepted.

Compaction thresholds, measured savings and estimation observations are extended by
[ADR-010](ADR-010-adaptive-compaction-and-estimation.md).
Large-window tool retention and comparable prefix estimates are extended by
[ADR-012](ADR-012-prefix-overlap-and-tool-headroom.md).

## Context

Small context windows could be exhausted before a user message was sent. Tool definitions had
an advisory budget, but ten required App tools and all discovered tools bypassed it. Standing
instructions had independent fixed limits, and the skill catalogue could consume 12,288 tokens.
History compaction cannot solve a request whose instructions and schemas already fill the window.

## Decision

`AdaptiveContextPolicy`, behind `IAdaptiveContextPolicy`,
owns the window-dependent budgets, instruction variants and admission priorities, tool ranking,
and preservation or replacement of the previously offered tool set. It is a transient Pure.DI
service with interface dependencies. No model-name heuristics or additional settings are needed.

Source readers collect data, the instruction composer serializes policy decisions, and the context
planner remains the final transport gate. These consumers do not implement competing adaptive rules.

Use the connection's resolved context and output reserve. Keep project rules and the current user
message intact. Prefer authored compact application instructions and bounded memory/skill indexes
on small windows. Missing capabilities are discovered progressively. Only definitions involved
in the current protocol continuation can bypass the schema share, and the complete request must
still pass the planner.

Choose standing variants once per run. Keep the order and schemas of a fitting tool set across
steps, allowing intentional changes for discovery, permission changes and pressure. Do not embed
the changing omitted-tool catalogue into the search schema. Preserve surviving tools' order when
replacement is necessary. A changed prefix is reported through the existing usage tracker;
provider cache hits are not guaranteed by application-level prefix stability.

## Consequences

- Small windows receive fewer tools and shorter application instructions, rather than a fixed
  control catalogue that consumes all input capacity.
- Search and asking the user have priority, but an individually oversized schema is not forced
  into the request merely because it was discovered.
- Searches in a batch share pending discovery priorities, consumed once by the next selection. Actual calls in the current turn
  are protected independently until their protocol context is compacted away.
- Oversized user rules remain visible in the preview and cause an actionable local failure.
- The project preview uses the project/default connection; a chat's explicit connection can differ.
- Tests cover window and output-reserve changes, complete requests with actual App schemas,
  cache-prefix preservation, discovery, pressure and oversized mandatory content.

Details and exact budgets are in [the context selection document](../21-tool-selection-and-adaptive-compaction.md).
