# MCP integration

Status: Accepted

> Current implementation (2026-09-07): a built-in stdio server with `process_run`, a streaming Chat Completions agent loop, confirmations, and call history have been implemented. See [default tools](16-default-mcp-tools.md). The descriptions of other servers, transports, and the Responses API below relate to the target architecture; the early preview sections reflect previous stages.


## Current configuration UI status

Project settings store MCP server bindings and per-tool policies. For the built-in `Default tools`, stdio, discovery, and execution are implemented; the `Discover tools` button in project settings loads tools and lets you define a policy. For third-party servers, only the configuration is saved: their processes and HTTP transports are not opened yet.

## Version and negotiation

The client uses the official C# SDK for Model Context Protocol and negotiates the protocol revision through MCP initialization. Draft features cannot be assumed without a negotiated capability.

Main standards:

- [MCP specification](https://modelcontextprotocol.io/specification/2025-11-25)
- [MCP Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP Authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)

## Transports

### stdio

Used for local History and FileSystem servers. The Host launches only pre-registered executable and arguments. The launch command is never formed from model output or browser requests.

### Streamable HTTP

Used for remote MCP servers. Legacy SSE is not used for new connections. For protected servers, the standard OAuth flow is implemented.

## Lifecycle

1. Create a transport.
2. Perform MCP initialization and store the negotiated capabilities.
3. Request `tools/list` with full pagination.
4. Normalize and cache descriptors.
5. Subscribe to `notifications/tools/list_changed` if `listChanged` is declared.
6. On a notification, re-run the list, recompute schema hashes, and re-evaluate policies.
7. Properly close the session and transport.

### Servers in chat runs

A turn opens its tool session before its first model step, so how servers start decides how long
the person waits for the first token. `CompositeToolSessionFactory` therefore:

- **Opens all servers together.** The built-in, application and user-configured servers start in
  parallel; a turn waits for the slowest one, not for their sum.
- **Leaves out a server that fails.** A server that cannot start — a missing executable, an error, or
  no answer within 45 seconds — is logged as `McpServerUnavailable`, reported in
  `IToolSession.Failures`, and named to the model through the `run.unavailable-servers` instruction.
  Its tools are missing for that turn; the turn itself goes on. A start that runs out of time fails
  with a `TimeoutException` naming the server, never with a cancellation: the run dispatcher treats a
  cancellation as Stop only when the turn's own token was cancelled, and any other one fails the run
  with an error instead of pausing it silently.
- **Keeps user-configured servers running between turns.** `ExternalMcpServerPool` leases a running
  server to one session at a time and takes it back when the session closes. An idle server is
  stopped after 15 minutes, at most two are kept per configuration, and servers are kept per user and
  per configuration fingerprint (command, arguments, directory, environment and credential, saved
  secrets included), so a changed configuration starts a new process and retires the old ones. A
  start that the turn stops waiting for still finishes and is kept for the next turn.
- **Starts a server only when it is needed.** The last `tools/list` answer of each user-configured
  server is stored in `mcp-tools.json` beside `settings.json`, under the configuration's fingerprint.
  A server with a stored list is offered from it without being started; the first call of one of its
  tools leases the server from the pool, reporting "Starting the MCP server …" as progress while it
  starts, and the list the server returns replaces the stored one for later turns. A server that
  cannot start then fails only that call, as an error result the model reads: nothing was sent, so
  nothing happened. A server without a stored list, or whose stored schemas no longer build, is
  started when the session opens.

Discovery from settings (`/api/mcp/tools/discover`) starts a private server for the configuration it
was given, saved or not, and stops it afterwards; it does not use the pool.

## Tool identity

A name is unique only within a single server. The internal key:

```text
ToolIdentity = ConfiguredMcpServerId + ToolName + ToolSchemaHash
```

`serverInfo.name` is not used as a unique identifier. For transmission to OpenAI, a safe alias is used, for example:

```text
mcp_{shortServerId}__{normalizedToolName}
```

The registry stores the reverse alias → original ToolIdentity mapping.

## Tool descriptor

Saved without loss:

- `name`;
- `title`;
- `description`;
- `inputSchema`;
- `outputSchema`;
- `annotations`;
- `execution`;
- `_meta`, when required for a round trip.

JSON Schema is validated before the tool is shown to the model. An invalid tool is disabled with diagnostic output.

## Tool annotations

Supported standard hints:

- `readOnlyHint`;
- `destructiveHint`;
- `idempotentHint`;
- `openWorldHint`.

Annotations are untrusted hints, not an ACL. They are used for UI and risk assessment only after the server's trust level is determined. The project policy remains mandatory.

## System and agent connections

### System

History/Project MCP is called by the application layer and is not transmitted to the model. Its tools serve projects, chats, branches, messages, and the audit log.

### Agent

Transmitted to the model after filtering:

```text
tools/list
→ schema validation
→ trust evaluation
→ project policy
→ alias mapping
→ provider tool definitions
```

## History MCP

Minimum tools:

```text
project_list, project_get, project_create, project_update, project_delete
chat_list, chat_get, chat_create, chat_delete, chat_copy
branch_list, branch_create, branch_rename, branch_move_head
message_append, message_get_path, message_search
audit_list
```

For reads, additional resources are published:

```text
project://{projectId}
project://{projectId}/chats/{chatId}
project://{projectId}/chats/{chatId}/messages/{messageId}
project://{projectId}/audit
```

## FileSystem MCP

Each tool has a single clear operation and correct annotations. A multi-purpose `filesystem` with an `operation` parameter is not used: individual tools are easier to authorize and explain to the user.

`edit` accepts an expected content hash and a structured patch/replacements. `write` does not replace an existing file without an explicit parameter and corresponding policy. `delete` is considered destructive regardless of annotations.

## OAuth for HTTP MCP

OAuth 2.1, RFC 9728 Protected Resource Metadata, RFC 8414/OIDC discovery, RFC 8707 Resource Indicators, and PKCE S256 are supported. Scopes are requested incrementally, for example:

```text
files:read
files:write
files:delete
history:read
history:write
```

The Host handles `401` and `403 insufficient_scope` via `WWW-Authenticate`. Broad wildcard scopes are not requested by default.

# Global MCP configuration

MCP servers are global and available to all projects. The first iteration implements only JSON storage and configuration UI:

- transports `StreamableHttp` and `Stdio`;
- enable/disable;
- server-wide policy `Allow`, `Ask`, or `Deny`;
- command, arguments, working directory, and environment variables for stdio;
- write-only credential for Streamable HTTP.

Chat runs connect and call third-party servers as described in [Servers in chat runs](#servers-in-chat-runs). The built-in `Default tools` are available automatically; the Host manages their execution, and tool discovery and per-tool policies are available in project settings.
