# AI

A local client for working with OpenAI and OpenAI-compatible AI endpoints. The interface runs in Blazor WebAssembly, while the local ASP.NET Core Host stores credentials and performs response generation.

Key features:

- streaming chat with Markdown;
- built-in MCP tool `process_run` with confirmations, policies, and call history;
- multiple AI endpoints and credential sets;
- projects with independent security settings;
- Git-like branching of chat history;
- local JSON storage based on immutable nodes;
- Pure.DI and interface dependencies without static application services.

## Documentation

Starting point: [docs/README.md](docs/README.md).

Main documents:

- [Product requirements and boundaries](docs/01-product-requirements.md)
- [Architecture](docs/02-architecture.md)
- [Domain model](docs/03-domain-model.md)
- [JSON storage](docs/04-storage.md)
- [MCP integration](docs/05-mcp-integration.md)
- [Default tools and process execution](docs/16-default-mcp-tools.md)
- [Security](docs/06-security.md)
- [AI endpoints and agent loop](docs/07-ai-and-agent-loop.md)
- [Implementation plan](docs/08-implementation-plan.md)
- [Testing strategy](docs/09-testing.md)
- [Operations and diagnostics](docs/10-operations.md)
- [Implementation progress](docs/11-implementation-progress.md)

Accepted architectural decisions are in [docs/decisions](docs/decisions).

## Automation

Repository automation is implemented as a separate .NET application in [build](build). It follows the same Pure.DI target-oriented approach as `dotnet-matrix/build`.

```powershell
dotnet run --project build -- build
dotnet run --project build -- test
dotnet run --project build -- verify
dotnet run --project build -- publish
dotnet run --project build -- publish-desktop --runtime win-x64
```

The desktop app (Windows, macOS, Linux) runs the same server in-process and shows the UI in the system web view; see [Desktop app](docs/23-desktop.md).

`verify` is the standard local and CI validation command. Command output is written to `artifacts/logs`.

## Install the web app in Chrome

```powershell
dotnet run --project src/AI.Host -- --serve-web --urls http://localhost:52173
```

Open `http://localhost:52173` in Chrome, then choose **Install AI Client** from Chrome's menu. The installed app opens in its own window and uses the same icon as the desktop app. Keep the Host running while using it; projects, chats, and generation are served by the Host. Chrome can install it from `localhost` over HTTP; access from another device requires HTTPS.

## Rider

Shared Rider run configurations are stored in [`.run`](.run). Select one from Rider's run-configuration menu:

- `AI Host` starts the local application at `http://localhost:52173` in Development mode;
- `Verify AI` builds the solution and runs the fast unit test suite;
- `Publish AI` publishes the Host to `artifacts/publish`.

## Data format

Backward compatibility of formats has been removed. Use a new directory via `AI_CLIENT_DATA_DIRECTORY`. Details: [architecture](docs/02-architecture.md) and [storage](docs/04-storage.md).
