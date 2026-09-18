# Product requirements and boundaries

Status: Accepted

## Purpose

AI.Client is a local user client for conversing with AI models and running agent scenarios. All external tools are connected through MCP. Work is performed within a project, which defines the context, available endpoints, MCP servers, and security rules.

## Functional requirements

### Projects

- A project has an ID, name, description, and creation/modification dates.
- A project specifies the default AI endpoint and model.
- A project contains system instructions for its chats.
- A project defines the list of connected MCP servers.
- A project stores a separate policy for each MCP tool.
- A project specifies the directories accessible to the FileSystem MCP server.
- Any chat belongs to exactly one project.
- Moving a chat between projects is performed by copying with new IDs and re-checking the policy.

### Chats

- Streaming responses.
- Markdown, fenced code blocks, tables, and safe links.
- Cancellation of the current response.
- Re-running a failed response without re-executing completed tool calls.
- Saving the model, endpoint, usage, and tool results for each run.
- Full restoration of history after restart.

### Branching

- A branch is created from any message.
- Common history of branches is not duplicated.
- Editing an old message creates a new branch or a new history path.
- Each branch has a stable ID, name, and reference to its head message.
- Deleting a branch does not delete nodes reachable from other branches.

### AI endpoints

- Multiple endpoint profiles are supported.
- The primary OpenAI protocol is the Responses API.
- Chat Completions fallback is supported for compatible providers.
- A profile contains the Base URI, protocol type, default model, custom headers, and a credential reference.
- Endpoint capabilities are checked by capability probe and can be corrected manually.
- Credentials are not transmitted to WASM and are not stored in the project JSON.

### MCP

- Streamable HTTP and local `stdio` are supported through the Host.
- Tools are discovered via `tools/list`.
- Catalog changes are handled through `notifications/tools/list_changed`.
- Tool names are separated by the stable ID of the configured server.
- History system MCP tools are not transmitted to the AI model.
- Agent MCP tools are transmitted to the model only after the project policy has been applied.

### FileSystem MCP

Required tools:

- `read`;
- `write`;
- `edit`;
- `delete`;
- `list`;
- `search`.

The server must verify allowed roots, normalize paths, prevent path traversal and symlink escape, enforce size limits, and maintain an audit log.

## Non-functional requirements

- Target framework: `net10.0`.
- UI: Client-Side Blazor WebAssembly.
- Hosting: local ASP.NET Core Host serving WASM from the same origin.
- DI: Pure.DI and Pure.DI.MS.
- Application and domain code uses interfaces and instances.
- Static application services and service locator are forbidden.
- Private `static SetupDI()` is allowed only as a compile-time marker of the Pure.DI source generator.
- All persisted JSON documents are versioned.
- All mutating operations must be atomic or safely recoverable.
- The UI must not block during AI or MCP streaming.
- Automated tests are unit tests and use xUnit, Shouldly, and Moq.
- Tests must be fast, deterministic, and independent of the file system, network, processes, credentials, OS, and other elements of the execution environment.
- Integration and end-to-end tests are not part of the automated test suite.

## Out of scope for the first release

- Multi-user collaboration.
- Cloud synchronization of projects.
- Execution of arbitrary shell commands.
- Native desktop shell/WebView.
- Automatic access to all tools of a trusted server.
- Full event sourcing.

## MVP criterion

The user launches one local Host, opens the WASM UI, creates a project, configures the endpoint and credentials, adds allowed directories and MCP servers, creates a chat, branches from a message, and launches an agent. Any new tool requires confirmation, and all tool calls and file changes remain in the audit log.
