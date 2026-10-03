# ADR-010: Adaptive compaction decisions and measured savings

Date: 2026-10-03. Status: accepted. Extends [ADR-009](ADR-009-adaptive-context-policy.md).

## Context

Tool and instruction selection already shared one adaptive policy, while compaction decisions
were spread across the agent and planner. They calculated percentages from different allowances.
Automatic checkpoints tested source size rather than actual savings; completed current-turn work
could only be shortened through the LLM fallback path. Character-based summary limits disagreed
with the UTF-8 estimator, and excerpts could omit an important diagnostic in the middle of output.

## Decision

Keep all window-dependent decisions in `AdaptiveContextPolicy`, behind `IAdaptiveContextPolicy`:
message allowance, trigger, retry growth, minimum savings, target size, recent keep allowance,
summary targets and estimation safety. The agent orchestrates, the compactor/projector formats
data, the checkpoint service applies accepted results, and the planner verifies transport input.
Executors do not define competing adaptive thresholds.

Apply automatic checkpoints only after measuring a sufficient positive reduction of the complete
message projection. Rejection keeps the previous checkpoint and original data. Rate-limit both
successful and unsuccessful automatic attempts by input growth. Preserve a fitting deterministic
projection across steps, and shorten completed current-turn work without requiring an LLM.
Keep the latest call/result group and current user request even when the gate must reject input.
Tag synthetic summaries explicitly so their user role does not make them a new user request.

Use a DI-resolved structured result projector for process, file and application output. Retain
bounded outcomes, diagnostics, paths and identifiers before excerpts. Stored chat data remains
unchanged. Use the shared estimator to bound summaries and calculate preview eligibility.

Compare estimates with provider-reported input usage. Keep bounded numeric samples in a singleton
behind `IContextEstimateSamples`, isolated by connection, endpoint and requested model. Increase
the safety reserve when recent reports exceed estimates; never lower the baseline based on
reports or train on estimates. The adaptive policy and projector remain transient services.

## Consequences

- Automatic compaction spends a model request only when there is enough source to try; its actual
  savings determine acceptance. A rejected request still costs the summary call already made.
- A new cut or checkpoint can change the provider cache prefix; subsequent fitting requests reuse
  the projection. Application prefix stability cannot guarantee provider cache hits.
- Structured retention is bounded and extractive. Full results remain available for rereading;
  extremely large diagnostics can still be shortened.
- Calibration protects against observed underestimation without an exact provider tokenizer.
  Samples are volatile and may reduce capacity until the anomalous sample leaves the recent set.
- Long-sequence tests cover 8K–128K windows, repeated growth, discovery, checkpoints, smaller-model
  transitions, compaction counts, prefix changes, mandatory text and complete parallel protocols.

Exact formulas and execution order are in [context selection and compaction](../21-tool-selection-and-adaptive-compaction.md).
