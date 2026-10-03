# ADR-012: Comparable prefix overlap and adaptive tool headroom

Date: 2026-10-03. Status: accepted.

## Context

A 250,000-token connection still limited schemas to 6,000 estimated tokens. The nearly full
catalogue changed nine times in 23 answer requests during progressive discovery. Requests with
stable definitions received approximately 98% cached input, while definition changes reduced
the hit rate. Separately, an unknown model's local prefix estimate was divided by provider input:
702,260 / 627,477 exceeded 100% and was clamped to a misleading cache eligibility figure.

## Decision

Keep all adaptive admission and retention logic in `AdaptiveContextPolicy` behind
`IAdaptiveContextPolicy`. Allocate schemas at most `min(24,576, U / 5)`, reduced by the entire
projected context and trailing guidance. Initial optional selection spends at most 80% of that
budget. Fit new required/discovered capabilities into the remaining room while retaining previous
order and definitions. A fitting carried set is not evicted because relevance or the admission
count changed. Permission removal, updated definitions, discovery that cannot append, and actual
history pressure can change the prefix. Protocol-required schemas remain protected and the
planner still validates the whole request.

Store estimated full input beside estimated shared prefix. Aggregate both only for records
carrying that denominator. Legacy records retain their original usage and change diagnostics
without fabricated denominators. Compare requests only within the same chat, branch, purpose,
connection, requested model and endpoint.

Show measured `Cached`, approximate `Prefix overlap`, and application `Prefix changes` separately.
Application overlap is not a prediction of provider cache eligibility. Label cumulative input and
output explicitly and mark estimated context fill. Reuse existing widget markup and styles.

## Consequences and verification

- Large windows can retain more schemas; there remains a bounded share and room for conversation.
- Small windows and growing history still force budgeted discovery or eviction.
- New overlap percentages become available after comparable requests; old ledgers need no migration.
- Unknown model estimates cannot inflate overlap by mixing local and provider counts.
- Numeric tool-selection diagnostics distinguish retention, expansion, pressure and catalogue changes.
- Tests cover progressive discovery at three window sizes, history pressure, schema/permission
  changes, model/connection/endpoint isolation, unknown tokenizers, legacy records and widget rendering.

Exact formulas are in [tool selection](../21-tool-selection-and-adaptive-compaction.md); usage
semantics are in [token usage](../28-token-usage.md).
