# ADR-006: Architecture simplification

Date: 2026-09-06. Status: accepted.

Duplicate connections, client-side coordination of runs, and branch recovery from history made changes harder and introduced race conditions.

Adopted: a single catalog of Connections and secrets; `ChatRunDispatcher` in the Application layer; one Submit command for Web and CLI; explicit branches; immutable message nodes; serialized mutations; SSE snapshots with revisions; separate `GlobalSettingsPanel` and `RunStateService`.

DI stays on Pure.DI. Interfaces for pure serializers and simple paths were removed. An additional container, a mediator, and a separate client-side state framework are not introduced.

By user decision, backward compatibility of APIs, old profiles, and files is not preserved. A new data directory is required. JSON is sized for a single Host; the unused-node collector and MCP execution remain separate tasks.

