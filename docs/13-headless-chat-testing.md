# Headless chat testing

## Назначение

`AI.Client.Cli` предоставляет внешний JSON-интерфейс для LLM, которая тестирует реальный chat streaming без Web UI. Сессия поддерживает несколько последовательных turn и хранит context отдельно от пользовательских чатов.

Маршрут запроса совпадает с приложением:

```text
Testing LLM -> AI.Client.Cli -> AI.Client.Host -> configured LLM endpoint
```

## Создание сессии

Host должен быть запущен. Имя проекта и endpoint берутся из сохранённых настроек AI.Client.

```powershell
dotnet run --project src/AI.Client.Cli -- session create `
  --project Uno `
  --endpoint Qwen3-Coder-480B `
  --host http://localhost:52173
```

Если `--endpoint` отсутствует, используется default endpoint проекта. stdout всегда содержит один JSON document. Progress и server diagnostics в stdout не записываются.

## Диалог

```powershell
dotnet run --project src/AI.Client.Cli -- session send `
  --session 019ff... `
  --message "Read Program.cs"

dotnet run --project src/AI.Client.Cli -- session send `
  --session 019ff... `
  --message "Now summarize the entry point"
```

Второй turn получает историю первого turn. Результат содержит `sessionId`, `turnId`, `status`, `finalText`, `chunkCount`, `firstTokenMs`, `durationMs` и `completionReason`.

Для воспроизводимой проверки Stop используется отмена того же HTTP streaming request:

```powershell
dotnet run --project src/AI.Client.Cli -- session send `
  --session 019ff... `
  --message "Write a long response" `
  --cancel-after-ms 1000
```

Команда возвращает `status: cancelled`, `completionReason: cancelled` и уже полученный partial text. Отменённый turn записывается в transcript, но не добавляется в дальнейший conversation context.

HTTP client использует `ResponseHeadersRead`: chunks доступны тестирующей LLM сразу, а не после закрытия всего response body.

## Просмотр и удаление

```powershell
dotnet run --project src/AI.Client.Cli -- session show --session 019ff...
dotnet run --project src/AI.Client.Cli -- session delete --session 019ff...
```

Файлы расположены в `%LocalAppData%\AI.Client\test-sessions\<session-id>`:

- `session.json` — состояние многошагового диалога;
- `transcript.jsonl` — append-only события сессии и turn.

Сессии не появляются в пользовательской истории проекта.

## Build automation

Та же команда доступна через build entrypoint:

```powershell
dotnet run --project build -- chat session create --project Uno
```

Rider содержит shared-конфигурацию `Headless Chat - Create`. Перед запуском при необходимости изменить project/host в `Program arguments`; Host запускается отдельной конфигурацией `AI.Client Host`.

## MCP и безопасность

При создании сессии сохраняется snapshot количества MCP servers, tool policies и directory grants проекта. Текущая версия не выполняет MCP tools: общий Agent Runtime и MCP Connection Manager ещё не реализованы. Команда `agent` возвращает `not_supported`, чтобы тест не создавал ложное впечатление проверки security enforcement.

После реализации общего Agent Runtime Web и CLI должны использовать один runtime. CLI не получит аргументов для расширения project grants; headless approval mode по умолчанию будет `deny`.

## Ограничения безопасности

- API key не возвращается в JSON и transcript.
- CLI передаёт только ID credential profile; Host получает protected credential локально.
- Prompt и final response находятся в test transcript по назначению механизма.
- Пользователь должен удалить test session, если transcript больше не нужен.
