# Logging

## Implementation

The Host uses the standard `Microsoft.Extensions.Logging` abstractions and two providers:

- Console — interactive diagnostics in Rider;
- a persistent JSONL file provider.

Logs are located in `%LocalAppData%\AI\logs` and are named `ai-client-YYYYMMDD.jsonl`. Retention is 14 days; stale files are deleted when the Host starts.

## Streaming events

- `ChatStreamStarted` (`1001`);
- `ContentChunkReceived` (`1002`);
- `ChatStreamCompleted` (`1003`);
- `ChatStreamCancelled` (`1004`);
- `ChatStreamFailed` (`1005`).

Each operation has a UUIDv7 `OperationId`. Structured fields live in the JSON object `Properties`: model, credential profile ID, chunk index, content length and elapsed time. Prompt/chunk content, API keys and Authorization headers are not logged.

The provider is fail-safe: complex values of system ASP.NET events are normalized into JSON primitives or strings, and a file-write error never interrupts the request pipeline.

## Diagnosing an unfinished generation

After reproducing, check the latest file:

```powershell
Get-Content "$env:LOCALAPPDATA\AI\logs\ai-client-$(Get-Date -Format yyyyMMdd).jsonl" | Select-Object -Last 100
```

If there are chunks but no `ChatStreamCompleted`, `ChatStreamCancelled` or `ChatStreamFailed`, the upstream enumeration did not finish. If `ChatStreamCompleted` is present but the Web stays in `Generating`, the problem is in the downstream/Web path.
