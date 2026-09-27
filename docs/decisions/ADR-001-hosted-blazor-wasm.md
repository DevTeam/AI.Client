# ADR-001: Hosted Blazor WebAssembly

Status: Accepted

Date: 2026-08-11

## Context

Client-Side WebAssembly is needed as the UI platform, but the browser sandbox cannot safely store AI credentials, launch `stdio` MCP processes, or gain arbitrary access to the local file system.

## Decision

Use a local ASP.NET Core `AI.Host` that serves `AI.Web` and exposes a same-origin API. The UI continues to run in the browser WASM. The Host stores credentials, calls AI endpoints, and manages MCP transports and processes.

## Consequences

Positive:

- secrets never leave the Host;
- `stdio` is available;
- no internal CORS;
- one self-contained executable launches the system;
- privileged operations are controlled centrally.

Negative:

- the PWA cannot perform agent operations without the Host;
- the Host becomes a security boundary;
- CSP, origin protection, and a strict MCP executable registry are required.

## Rejected alternative

Standalone static WASM with BYOK is rejected as the primary mode because of credential leakage, CORS, and the absence of `stdio`/filesystem access.

