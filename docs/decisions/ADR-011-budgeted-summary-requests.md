# ADR-011: Budgeted summary requests and separate quality evaluations

Date: 2026-10-03. Status: accepted. Extends [ADR-010](ADR-010-adaptive-compaction-and-estimation.md).

## Context

The main request respected the adaptive allowance, but summarization split input into fixed
40,000-character parts and sent them directly. A summary or merge could itself overflow a small
model. Output reserves were local planning values; compatible providers do not universally support
the same generation-limit parameter. Mock-based budget tests did not measure model retention.

## Decision

`IAdaptiveContextPolicy.ResolveSummary` owns summary input/output targets and work limits. The
summary writer measures the complete rendered user message, including instructions and part
labels, before every source or merge call. Source partitioning uses that allowance, preserves
Unicode scalar boundaries and covers the entire formatted source. A missing part, impossible
budget, failed call or non-converging merge returns no replacement. Cancellation propagates.
Manual history compaction, run checkpoints and planner fallback pass the actual connection.

Use model-bound views of `IContextTokenEstimator` for planning, tool/instruction admission,
summaries, calibration, usage estimates and prefix measurements. `IContextTextTokenizer` counts
recognized model text offline using Microsoft.ML.Tokenizers and packaged Cl100k/O200k vocabularies.
Unknown models or unavailable vocabularies retain the conservative UTF-8 estimate. Wire framing
is still estimated; the baseline and observed safety margins remain. Shared tokenizer lookup
is a bounded singleton; policies and estimators remain transient.

Summaries request explicit Goal, Constraints, Decisions, Evidence, Failures and Remaining work
sections. They retain data authority, reasons, paths, errors, rejected alternatives and pending
status. This is a prompt contract, not proof of semantic fidelity or a new storage schema.

Do not send unverified generation-limit fields. The output reserve remains a local allowance and
does not guarantee that a provider limits visible or reasoning output to that value.

Record numeric summary outcomes, calls, transmitted tokens and latency, and final-plan savings
and compaction reasons. Combine those with the existing usage/cost/cache ledger. Do not log source
text or credentials. Put opt-in live quality comparisons in a separate evaluation project outside
the normal solution/test run, with shared continuation fixtures and an explicit enable switch.

## Consequences

- Fixed character chunks no longer dictate support for small context windows.
- Summary calls are bounded to 64 per operation and four merge rounds; unmanageable source is
  left intact rather than partially summarized. Every source part must produce a usable result.
- An oversized model reply is bounded before reuse. That can lose detail; repeated-summary quality
  must be evaluated rather than inferred from budget tests.
- Local tokenization needs no HTTP call or secret, but model names and vocabularies must match.
  Exact text counts are not exact provider request counts.
- The two tokenizer vocabularies add distribution size and shared initialization memory.
- Normal tests remain offline; live evaluations report retention, tokens and latency and require
  deliberate endpoint/model configuration. Identifier recall is an initial quality signal, not a
  complete semantic judge.

See [context selection](../21-tool-selection-and-adaptive-compaction.md) and
[context evaluation](../31-context-evaluation.md) for formulas, schema and commands.

Tokenizer reference: [Microsoft's migration guide](https://github.com/dotnet/machinelearning/blob/main/docs/code/microsoft-ml-tokenizers-migration-guide.md).
