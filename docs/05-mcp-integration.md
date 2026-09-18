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

Connecting and calling third-party servers remains deferred. The built-in `Default tools` are available automatically; the Host manages their execution, and tool discovery and per-tool policies are available in project settings.
