# ADR-003: Per-tool MCP permissions

Status: Accepted

Date: 2026-08-11

## Context

MCP ToolAnnotations convey risk hints, but the specification requires treating them as untrusted for unknown servers. Generic `read/write/delete` categories do not describe arbitrary tools accurately enough.

## Decision

Each project stores `Allow`, `Ask`, or `Deny` separately for a ToolIdentity that combines the configured server ID, the tool name, and the schema hash. A new or changed tool receives `Ask`. Directory grants, OAuth scopes, and server-side ACLs further restrict the call.

## Consequences

Positive:

- explicit user control;
- a schema change invalidates the previous approval;
- identical tool names from different servers are not mixed up;
- the model cannot expand its permissions through annotations.

Negative:

- more initial approvals are required;
- a convenient policy UI is needed;
- a large number of tools requires filtering and bulk operations that must not silently weaken the default `Ask`.

## Note

Annotations are used to explain the risk and to support safe planning, but they are not an authorization. HTTP MCP authorization is implemented through the standard OAuth 2.1 flow; local `stdio` relies on process and environment security.

