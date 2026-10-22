# ADR-015: Server projects by layer

Status: Accepted

Date: 2026-10-22

## Context

`AI.Server` is one project of about 33,000 lines holding four layers: Domain (1.6k), Application (18k),
Infrastructure (11k) and Hosting with updates and the command line (3k). The layer rule — Domain depends
on nothing, Application on Domain and the contracts, adapters on Application — is kept by convention
only, and the replaceable adapters of [ADR-014](ADR-014-replaceable-boundaries-and-user-scope.md) (a SQL
store, S3, another model protocol, sign-in) would land inside the same project and ship with every
executable.

The namespaces already follow the layers (`AI.Domain`, `AI.Application`, `AI.Infrastructure`,
`AI.Mcp.App`, `AI.Server.Hosting`), so files can move between projects without being renamed. A survey
found only a few places that cross the rule:

- `Application/Schedules/ChatScheduleStore` reads and writes schedule documents itself through
  `IFileSystem` and `IChatStoragePaths` — a JSON adapter living in Application;
- two Application types use `Microsoft.AspNetCore.StaticFiles` to name a file's media type;
- Infrastructure reads `ServerOptions` from Hosting (`InstanceStorageLocation`), and the in-process app
  tool server reaches into Hosting (`AppMcpServerHost`).

## Decision

Split `AI.Server` into five projects by layer, each referencing only the ones below it:

| Project | Holds | References |
| --- | --- | --- |
| `AI.Domain` | `Domain/` | — |
| `AI.Application` | `Application/` — scenarios and every port; the built-in skills it reads | `AI.Domain`, `AI.Contracts` |
| `AI.Infrastructure` | storage, settings, credentials, the OpenAI-compatible client, workspace, usage, logging | `AI.Application` |
| `AI.Tools` | MCP tool sessions, the tool environment, the in-process app tools (`AI.Mcp.App`) | `AI.Application` |
| `AI.Server` | Hosting (HTTP/SSE, middleware, endpoints), updates, the command line, the Pure.DI setups | all of the above |

Each package goes to the lowest project that uses it, so a package the Application layer does not need
is not visible to it. The compositions stay in `AI.Server` and keep being linked as source by the
executables and the tests; every type they bind is public, as it is today.

A future adapter is a project of its own referencing only `AI.Application` — `AI.Storage.Sql`,
`AI.Storage.S3`, `AI.Models.Anthropic`, `AI.Auth.Oidc` — and the executable that wants it binds it.

Not done, deliberately:

- **No slicing by feature** (chats, runs, tools, resources). The run dispatcher uses chats, tools,
  resources, settings and usage; feature projects would either form cycles or need a wide public surface
  between them.
- **No finer split** — a project per folder, or the ports in a project apart from the scenarios. It adds
  projects to maintain without moving any boundary.

### Steps

Each step builds, passes `dotnet run --project build -- test-all` and is one commit; files move with
`git mv`, so their history follows them.

1. Remove the crossings: a schedule-document port in Application with its JSON adapter in
   Infrastructure; media-type naming moved out of Application; storage options passed to Infrastructure
   without `ServerOptions`; the app tool server free of Hosting.
2. `AI.Domain`.
3. `AI.Application`, with the built-in skill documents it embeds.
4. `AI.Infrastructure` and `AI.Tools`; what remains is `AI.Server`.

## Consequences

Positive:

- the compiler keeps the layer rule: Application cannot reach an adapter, and an adapter cannot reach
  Hosting;
- a new adapter is a small project with one reference, and an executable that does not bind it does not
  ship it;
- a change to one layer rebuilds only the projects above it.

Negative and accepted:

- five projects instead of one to keep in the solution and in the documentation;
- types that were reachable inside one assembly now cross assembly boundaries, so the few `internal`
  ones used across layers become public or move.
