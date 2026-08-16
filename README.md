# AI.Client

Локальный клиент для работы с OpenAI и OpenAI-совместимыми AI endpoints. Интерфейс выполняется в Blazor WebAssembly, а локальный ASP.NET Core Host хранит credentials и выполняет генерацию ответов.

Ключевые возможности:

- streaming-чат с Markdown;
- настройки MCP и политик инструментов (исполнение MCP пока не реализовано);
- несколько AI endpoints и наборов credentials;
- проекты с независимыми настройками безопасности;
- Git-подобное ветвление истории чата;
- локальное JSON-хранилище на основе неизменяемых узлов;
- Pure.DI и интерфейсные зависимости без статических application services.

## Документация

Начальная точка: [docs/README.md](docs/README.md).

Основные документы:

- [Требования и границы продукта](docs/01-product-requirements.md)
- [Архитектура](docs/02-architecture.md)
- [Доменная модель](docs/03-domain-model.md)
- [JSON-хранилище](docs/04-storage.md)
- [MCP-интеграция](docs/05-mcp-integration.md)
- [Безопасность](docs/06-security.md)
- [AI endpoints и agent loop](docs/07-ai-and-agent-loop.md)
- [План реализации](docs/08-implementation-plan.md)
- [Стратегия тестирования](docs/09-testing.md)
- [Эксплуатация и диагностика](docs/10-operations.md)
- [Ход реализации](docs/11-implementation-progress.md)

Принятые архитектурные решения находятся в [docs/decisions](docs/decisions).

## Automation

Repository automation is implemented as a separate .NET application in [build](build). It follows the same Pure.DI target-oriented approach as `dotnet-matrix/build`.

```powershell
dotnet run --project build -- build
dotnet run --project build -- test
dotnet run --project build -- verify
dotnet run --project build -- publish
```

`verify` is the standard local and CI validation command. Command output is written to `artifacts/logs`.

## Rider

Shared Rider run configurations are stored in [`.run`](.run). Select one from Rider's run-configuration menu:

- `AI.Client Host` starts the local application at `http://localhost:52173` in Development mode;
- `Verify AI.Client` builds the solution and runs the fast unit test suite;
- `Publish AI.Client` publishes the Host to `artifacts/publish`.

## Формат данных

Обратная совместимость форматов удалена. Используйте новый каталог через AI_CLIENT_DATA_DIRECTORY. Подробнее: [архитектура](docs/02-architecture.md) и [хранение](docs/04-storage.md).
