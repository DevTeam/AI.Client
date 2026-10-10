# Redesign analysis: three independently deployable components

Status: **Complete**. This directory holds the analysis produced by a four-analyst team plus the
lead's synthesis. It changes nothing in `src/`, `tests/` or `build/`; it is analysis only.

## Goal

Produce the material needed to plan a restructuring of AI Client into three independently
deployable components that may run on different servers:

1. `ai-models` — everything that talks to AI model providers;
2. `ai-core` — the store of projects, chats, settings and other metadata;
3. `ai-resources` — the store of resources (bytes), ready for alternative backing stores
   (object storage, S3).

The analysis must name the shared parts that become reusable components, fix the interfaces and
contracts between the components, describe the current state of the project, and give a target
design document with pros and cons.

## Documents

| File | Owner | Content |
| --- | --- | --- |
| [00-current-state-overview.md](00-current-state-overview.md) | Lead | Synthesised current state of the system, based on the area reports |
| [01-ai-models-current-state.md](01-ai-models-current-state.md) | Ada · AI models | `S1` — model clients, agent loop, context and compaction, tool calls, token usage |
| [02-persistence-current-state.md](02-persistence-current-state.md) | Bo · Storage | `S2` — metadata: projects, chats, branches, messages, runs, settings, secrets, memory, instructions, schedules, usage ledger |
| [03-resources-current-state.md](03-resources-current-state.md) | Cleo · Resources | `S3` — project file access, attachments, artifacts, reviews, previews, temporary directories |
| [04-contracts-and-shared-parts.md](04-contracts-and-shared-parts.md) | Dan · Contracts | `S4` + `S4-node`, `C1`–`C8` — shared parts extracted into reusable modules and the inter-component contract specification |
| [05-target-design.md](05-target-design.md) | Lead | Target decomposition, deployment topology, contracts, migration path, pros and cons, open questions |

## Conventions

- Documents are written in English, per the project language convention (`docs/README.md`).
- Seam and contract identifiers (`S1`–`S5` plus the machine-bound seam `S4-node`, contracts
  `C1`–`C8`) are defined in the team charter recorded in the chat and are used consistently across
  all documents. `C8` was added by decision D13 when the node-agent seam was fixed.
- Every statement about the current state carries a verifiable `path:line` reference to the source.
  Statements taken from `docs/` rather than from code are marked as such; unverified reasoning is
  marked `assumption:` and external facts are marked `external:`.

## Reading order

1. `00-current-state-overview.md` — where the system is today.
2. `01`–`03` — the per-area evidence, each with its seam table.
3. `04-contracts-and-shared-parts.md` — what is shared and what the boundaries look like.
4. `05-target-design.md` — the proposed design, its costs and its open questions.
