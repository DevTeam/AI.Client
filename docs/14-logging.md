# Logging

## Реализация

Host использует стандартные `Microsoft.Extensions.Logging` abstractions и два provider:

- Console — интерактивная диагностика в Rider;
- постоянный JSONL file provider.

Логи расположены в `%LocalAppData%\AI.Client\logs` и именуются `ai-client-YYYYMMDD.jsonl`. Хранение — 14 дней; устаревшие файлы удаляются при запуске Host.

## Streaming events

- `ChatStreamStarted` (`1001`);
- `ContentChunkReceived` (`1002`);
- `ChatStreamCompleted` (`1003`);
- `ChatStreamCancelled` (`1004`);
- `ChatStreamFailed` (`1005`).

Каждая операция имеет UUIDv7 `OperationId`. Структурированные поля находятся в JSON object `Properties`: model, credential profile ID, chunk index, content length и elapsed time. Содержимое prompt/chunk, API key и Authorization headers не логируются.

Provider является fail-safe: сложные значения системных ASP.NET events нормализуются в JSON primitives или строки, а ошибка файловой записи никогда не прерывает request pipeline.

## Диагностика незавершённой генерации

После воспроизведения проверить последний файл:

```powershell
Get-Content "$env:LOCALAPPDATA\AI.Client\logs\ai-client-$(Get-Date -Format yyyyMMdd).jsonl" | Select-Object -Last 100
```

Если есть chunks, но нет `ChatStreamCompleted`, `ChatStreamCancelled` или `ChatStreamFailed`, upstream enumeration не завершился. Если `ChatStreamCompleted` присутствует, а Web остаётся в `Generating`, проблема находится в downstream/Web path.
