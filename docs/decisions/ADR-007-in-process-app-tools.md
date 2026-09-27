# ADR-007: Application management tools in an in-process MCP server

Status: Accepted

Date: 2026-09-15

## Context

The model needs access to the application's own data: create a chat from within a chat, set up a project for a repository, explain a tool denial, and maintain history. The data is available only through the Host's application services, which own revision checks, atomic writes, and event delivery.

The built-in server `AI.Mcp.BuiltIn` is a separate process that runs over stdio. It has no access to those services. The "child process talks back to the Host over loopback HTTP" option adds an authentication boundary in exactly the place where the [security model](../06-security.md) already calls the web-to-stdio bridge a privileged risk.

A separate concern is self-escalation of privileges: a tool that changes directory grants and tool policies is, by default, a hole.

## Decision

### In-process server on the standard protocol

The application tools live in a new MCP server `AI.Mcp.App`, built on the `ModelContextProtocol` library. It speaks the regular protocol — `tools/list`, `tools/call`, JSON schemas, result validation against the output schema — and differs only in transport: a pair of in-memory `System.IO.Pipelines` channels instead of the standard child-process streams.

A hand-rolled `IToolSession` implementation that bypasses the protocol is rejected: it would introduce a second, divergent model of a tool and skip the schema validation that the policies rely on.

`CompositeToolSessionFactory` merges the stdio server and the in-process one into a single set of tools. `IToolSessionFactory.OpenAsync` accepts a set of server IDs, so a disabled server is not started at all.

### Five tools by risk level

`app_read`, `app_chats`, `app_runs`, `app_projects`, `app_security`. The tool boundary matches the boundary of what the user allows with a single `Allow` button. Finer slicing by operation would bloat the tool list, while one universal tool would make `Allow` unjustifiably broad.

### No privilege-escalation restrictions

The agent can do everything the user can do through the UI, including extending its project's grants and changing the policies of `app_*` tools. The application is local and single-user; the agent already runs with the user's account permissions and has `process_run`, so a prohibition at this tool level would create the appearance of a boundary that does not exist.

### A single change signal instead of an addressed event

The `/api/runs/events` stream receives a `data-changed` frame with no payload. The client re-reads what it shows. An event that names the resource and its revision would require keeping the event schema in lockstep with every resource; for a local application, extra re-reads are cheaper.

## Consequences

Positive:

- revisions, atomicity, and events are inherited by the tools without extra work;
- one session implementation serves all servers, and a new transport does not multiply code;
- the open UI sees the agent's changes without reloading;
- third-party MCP servers connect through the same path as the in-process one.

Negative:

- with an `Allow` policy, `app_security` can extend its own permissions and enable any MCP server — i.e., go beyond the project without confirmation; the only protection is the default `Ask` policy;
- recursive chat spawning is limited only by the agent loop limits and the stop button;
- the idempotency journal lives in process memory and does not survive a Host restart;
- a single signal forces the client to re-read more than actually changed.

Related decisions: [ADR-003](ADR-003-per-tool-permissions.md) on tool policies, [default tools](../16-default-mcp-tools.md), [App tools description](../17-app-tools.md).

