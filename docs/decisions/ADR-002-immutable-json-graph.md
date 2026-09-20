# ADR-002: JSON graph of immutable nodes

Status: Accepted

Date: 2026-08-11

## Context

History must be local, portable, human-readable, and support Git-like branches without copying the shared part. The user has chosen several JSON files instead of a database.

## Decision

Each message is stored as a separate immutable JSON node with a `ParentId`. `chat.json` holds metadata, revision, and branch refs. Large results are stored content-addressed. A new write first creates the node, then atomically moves the branch head.

## Consequences

Positive:

- branches do not duplicate history;
- appending rewrites almost no data;
- a partial failure leaves a safe orphan;
- the format is easy to export and diagnose;
- integrity can be verified by hash.

Negative:

- building a path requires a walk and an index;
- a garbage collector is required;
- multi-writer updates require a revision check;
- search without an additional index is slower than a database.

## Rejected alternatives

- A single nested JSON per chat: too much rewriting and a large blast radius on corruption.
- Event sourcing: useful, but excessive for the first release.
- SQLite: reliable, but does not match the adopted requirement of a portable JSON tree.

