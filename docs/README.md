# AI Documentation

The documents describe the agreed-upon architecture and are the source of truth for implementation. When a key decision changes, the corresponding ADR is created or updated first, then the affected documents are updated in lockstep.

## Reading order

1. [Product requirements and boundaries](01-product-requirements.md)
2. [Architecture](02-architecture.md)
3. [Domain model](03-domain-model.md)
4. [JSON storage](04-storage.md)
5. [MCP integration](05-mcp-integration.md)
6. [Security](06-security.md)
7. [AI endpoints and agent loop](07-ai-and-agent-loop.md)
8. [Implementation plan](08-implementation-plan.md)
9. [Testing strategy](09-testing.md)
10. [Operations and diagnostics](10-operations.md)
11. [Implementation progress](11-implementation-progress.md)
12. [UX decisions](12-ux-decisions.md)
13. [Headless chat testing](13-headless-chat-testing.md)
14. [Concurrent chat runs](14-concurrent-chat-runs.md)
15. [Logging](14-logging.md)
16. [Composer rules](15-composer-rules.md)
17. [Default MCP tools](16-default-mcp-tools.md)
18. [Application management tools](17-app-tools.md)
19. [Compact turn view](18-compact-turn-view.md)
20. [Asking the user (`ask_user`)](19-ask-user.md)
21. [LLM context management](20-context-management.md)
22. [Tool selection and adaptive compaction](21-tool-selection-and-adaptive-compaction.md)
23. [Hidden model instructions and run completion](22-hidden-model-instructions-and-run-completion.md)
24. [Desktop app](23-desktop.md)
25. [Chat artifacts and reviews (proposal)](24-chat-artifacts.md)
26. [Long-term memory and project instructions](25-memory-and-instructions.md)
27. [Skills](27-skills.md)

## Accepted decisions

- [ADR-001: Hosted Blazor WebAssembly](decisions/ADR-001-hosted-blazor-wasm.md)
- [ADR-002: JSON graph of immutable nodes](decisions/ADR-002-immutable-json-graph.md)
- [ADR-003: Per-tool MCP permissions](decisions/ADR-003-per-tool-permissions.md)
- [ADR-004: Responses API as the primary OpenAI protocol](decisions/ADR-004-openai-responses-api.md)
- [ADR-005: Fast unit tests on xUnit](decisions/ADR-005-unit-testing.md)
- [ADR-006: Architecture simplification](decisions/ADR-006-architecture-simplification.md)
- [ADR-007: Application management tools in an in-process MCP server](decisions/ADR-007-in-process-app-tools.md)
- [ADR-008: Desktop app on Avalonia with the system web view](decisions/ADR-008-desktop-app.md)

## Document status

All listed documents have the status `Accepted` and capture decisions made before implementation began. NuGet package versions must be set centrally in `Directory.Packages.props`; the latest compatible stable version is chosen during implementation and pinned by the lock file.

The actual state of work, verification results, and the next increment are recorded in [Implementation progress](11-implementation-progress.md). It has the status `Active` and is updated after each completed increment.

## Language convention

All UI text, source-code comments, identifiers, technical messages, and project documentation introduced by the project are written in English.

Current decisions: [ADR-006](decisions/ADR-006-architecture-simplification.md).

- [Default MCP tools](16-default-mcp-tools.md) — process execution, permissions, and CLI.
