# Implementation plan

## Completed: local chat history

- A chat belongs to a project and contains a message tree with an explicit parent-message reference.
- The selected branch builds the context from the root to the leaf message; alternative responses remain separate branches.
- Chats are stored locally as JSON in the project directory with a revision and atomic temporary-file replace.
- Same-origin API and a minimal UI have been added: create a chat, select a chat, and save a user/assistant pair after live-chat completion.

Status: Accepted

The actual execution of stages and verification results are kept in a separate log, [Implementation progress](11-implementation-progress.md). A stage is considered complete only after all its exit criteria have been satisfied.

Each stage ends with a working vertical slice and verifiable exit criteria.

## Stage 0. Foundation

- Create the solution and projects.
- Configure `net10.0`, nullable, analyzers, and central package management.
- Connect Pure.DI/Pure.DI.MS.
- Introduce strongly typed UUID v7 IDs, a clock, and result/error contracts.
- Add xUnit test projects with Shouldly and Moq.
- Add CI build and a fast unit-test suite without external resources.
- Use a dedicated build application following the `dotnet-matrix/build` model: a Pure.DI composition root, an interface target per operation, and CLI commands for build, test, verify, and publish.

Done when the solution builds, composition roots are verified, Domain does not depend on Infrastructure, and `dotnet run --project build -- verify` builds the solution and runs the fast unit-test suite.

## Stage 1. Projects and local storage core

- Implement the Project aggregate.
- Implement JSON envelopes, atomic writer, and schema validation.
- Implement project CRUD.
- Add directory grants, MCP bindings, and per-tool policies.
- Add recovery after an unfinished atomic write.

Done when a project survives a restart, a revision conflict is detected, and secrets are absent from JSON.

Current progress: project metadata and security settings CRUD, revision conflict, and recovery are implemented. A temporary live-chat preview for verifying an OpenAI-compatible endpoint without saving credentials has been added. MCP configuration is saved locally, but the transport is not launched yet. The next stage is endpoint profiles and protected credential storage in the Host.

## Stage 2. Hosted WASM shell

- Create AI.Client.Host and AI.Client.Web.
- The Host serves WASM from the same origin.
- Configure Pure.DI composition roots following the Matrix.Web model.
- Implement the project list/settings UI.
- Add CSP, session, and origin/CSRF protection.

Done when one Host executable opens the UI and project CRUD works through a versioned API.

## Stage 3. Endpoints and credentials

- Implement the credential store in the Host.
- Implement endpoint profiles.
- Add an OpenAI Responses adapter.
- Add a Chat Completions fallback.
- Add a safe connection/capability check.

Done when the key never appears in WASM, network responses, or logs, and two endpoint profiles can be switched between.

## Stage 4. Streaming Markdown chat

- Implement Chat and immutable MessageNode.
- Implement streaming AgentEvents.
- Add cancel, incomplete, and failed states.
- Add Markdig and HtmlSanitizer.
- Save endpoint/model snapshots and usage.

Done when a chat is restored after a restart, Markdown is safe, and cancellation does not produce a completed response.

## Stage 5. Branching

- Implement refs and branch head updates.
- Fork from any node.
- Editing via a new path.
- Garbage detection for unreachable nodes.
- Copying a chat between projects with ID remap.

Done when shared history is not duplicated and a conflict between two head updates is detected by revision check.

## Stage 6. MCP connection manager

- Connect the official MCP C# SDK.
- Implement stdio and Streamable HTTP transports.
- Implement initialization, pagination, and list changed.
- Introduce ToolIdentity, alias registry, and schema hash.
- Add trust status and a tool catalog UI.

Done when two servers with the same tool name are distinguished correctly and a schema change resets the approval.

## Stage 7. History MCP

- Move project/chat repositories into a separate MCP server.
- Implement system tools and resources.
- Switch Host repository adapters to MCP.
- Keep the local cache only as a recoverable UI cache.

Done when all projects and history are accessible through MCP, but history tools do not appear in the model tool list.

## Stage 8. Agent loop

- Convert MCP descriptors into provider tools.
- Implement validation, Allow/Ask/Deny, and approvals.
- Execute calls and save call/result IDs.
- Add limits, timeout, cancellation, and retry rules.
- Add the tool timeline.

Done when the model performs several MCP calls, stopping is safe, and a completed side effect is not repeated.

## Stage 9. FileSystem MCP

- Implement `read`, `write`, `edit`, `delete`, `list`, and `search`.
- Add canonical roots and argument constraints.
- Protect against traversal, symlink/junction escape, and TOCTOU.
- Implement hash-based optimistic edit and atomic write.
- Add structured results and audit.

Done when module-level security tests on a fake filesystem do not allow escaping the grants and edit rejects a stale hash.

## Stage 10. HTTP MCP authorization

- Implement Protected Resource Metadata discovery.
- Add RFC 8414/OIDC discovery, PKCE S256, and Resource Indicators.
- Add progressive scopes and `insufficient_scope` handling.
- Protect token storage and redaction.

Done when a remote MCP server connects through the standard OAuth flow without broad scopes.

## Stage 11. Reliability and UX

- Reconnect and interrupted-run recovery.
- Context compaction.
- Project-wide search.
- Export/import with schema validation.
- Backup/restore.
- Diagnostics bundle without secrets.
- PWA assets for the offline UI shell.

## Stage 12. Release readiness

- Full threat-model review.
- Dependency and vulnerability scan.
- Full fast unit-test suite independent of Windows and the local environment.
- Self-contained Host publish.
- Installation, update, and recovery documentation.

## Development order within a stage

1. Domain contract and tests.
2. Application use case.
3. Infrastructure adapter.
4. Pure.DI registration.
5. UI/API composition.
6. Security negative tests.
7. Documentation and acceptance check.

## Completed: Markdown in chat

- Responses and saved messages are rendered as Markdown through Markdig with extensions.
- Embedded HTML in Markdown is disabled, and the resulting HTML is additionally sanitized by HtmlSanitizer before being passed to `MarkupString`.
- The original Markdown text is saved in history, so re-rendering does not change chat data.

## Completed: chat branching

- The user selects any saved point through **Branch from this message**.
- A new send creates a child user/assistant pair and does not change the existing continuation.
- Only the path from the root to the selected point plus the new user message is sent to the OpenAI-compatible request.

## Completed: workspace UI

- The screen has been rebuilt as a three-panel workspace following the adopted reference: projects and chats on the left, the active conversation and composer in the center, project settings and endpoint profiles on the right.
- The main scenario is now linear: select a project, create a chat, select an endpoint, write a message.
- The endpoint profile is edited as a separate card with labelled fields and explicit save status.
- Security and MCP are separated from the daily chat scenario and shown as advanced settings.

