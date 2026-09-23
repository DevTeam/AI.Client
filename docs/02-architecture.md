# Architecture

AI.Client.Server holds the whole server side: Domain (rules and invariants), Application (scenarios and interfaces to external resources), Infrastructure (HTTP, JSON, storage, secret protection, MCP tools) and Hosting (the HTTP/SSE API as `IEndpointModule`s, `AiClientServer`, the shared System.CommandLine options). Web depends only on Contracts, the Host-Web contract. Executables stay thin: AI.Client.Host parses its command line and runs the server. Everything is composed with Pure.DI; compositions are shared as source — `Contracts/Composition.cs`, `Server/Composition.cs` and `Server/CommandLine/Composition.cs` are internal setups that consumers link and extend with `DependsOn`.

`GlobalSettings.Connections` is the unified connections catalog. A project stores a `ConnectionId`; a chat can override it. Secrets are available through `IGlobalSecretStore`. Duplicate project profiles have been removed.

Services receive dependencies through their constructors. Composition defines implementations and lifetimes. Pure serializers have become static functions; path resolvers are concrete classes. No additional DI container is needed.

`ChatRunDispatcher` lives in the Application layer. Web and CLI send a single `Submit` command: Send, Queue, Fork, or Replace. The server manages the queue, idempotency, context, and result recording. `ChatSynchronization` serializes chat changes, but network requests for different branches run in parallel.

`ChatRunHostedService` recovers interrupted runs and waits for workers when the Host stops. Deletion first stops workers. SSE delivers snapshots with revisions and allows coalescing of intermediate events.

`GlobalSettingsPanel` owns settings editing; `RunStateService` stores run snapshots. The composer sends a server command. Branches have explicit stable IDs and heads.

MCP execution and the agent loop are not yet implemented: settings are available. The API and formats have been changed without backward compatibility. See ADR-006.
