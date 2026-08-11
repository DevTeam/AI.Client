# AI.Client

Локальный клиент для работы с OpenAI и OpenAI-совместимыми AI endpoints. Интерфейс выполняется в Blazor WebAssembly, а локальный ASP.NET Core Host хранит credentials, вызывает AI API и подключает инструменты по Model Context Protocol (MCP).

Ключевые возможности:

- streaming-чат с Markdown;
- агентский цикл с MCP-инструментами;
- несколько AI endpoints и наборов credentials;
- проекты с независимыми настройками безопасности;
- Git-подобное ветвление истории чата;
- локальное JSON-хранилище на основе неизменяемых узлов;
- отдельный FileSystem MCP server с `Read`, `Write`, `Edit`, `Delete`, `List` и `Search`;
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

Принятые архитектурные решения находятся в [docs/decisions](docs/decisions).

