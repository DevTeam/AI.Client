# Документация AI.Client

Документы описывают согласованную архитектуру и являются исходным контрактом для реализации. При изменении ключевого решения сначала создаётся или обновляется ADR, затем синхронно обновляются затронутые документы.

## Порядок чтения

1. [Требования и границы продукта](01-product-requirements.md)
2. [Архитектура](02-architecture.md)
3. [Доменная модель](03-domain-model.md)
4. [JSON-хранилище](04-storage.md)
5. [MCP-интеграция](05-mcp-integration.md)
6. [Безопасность](06-security.md)
7. [AI endpoints и agent loop](07-ai-and-agent-loop.md)
8. [План реализации](08-implementation-plan.md)
9. [Стратегия тестирования](09-testing.md)
10. [Эксплуатация и диагностика](10-operations.md)
11. [Ход реализации](11-implementation-progress.md)
12. [UX decisions](12-ux-decisions.md)
13. [Composer rules](15-composer-rules.md)
14. [Инструменты по умолчанию](16-default-mcp-tools.md)
15. [Инструменты управления приложением](17-app-tools.md)
16. [Компактное представление хода в чате](18-compact-turn-view.md)
17. [Вопрос пользователю (ask_user)](19-ask-user.md)

## Принятые решения

- [ADR-001: Hosted Blazor WebAssembly](decisions/ADR-001-hosted-blazor-wasm.md)
- [ADR-002: JSON-граф из неизменяемых узлов](decisions/ADR-002-immutable-json-graph.md)
- [ADR-003: Права отдельно для каждого MCP-инструмента](decisions/ADR-003-per-tool-permissions.md)
- [ADR-004: Responses API как основной OpenAI-протокол](decisions/ADR-004-openai-responses-api.md)
- [ADR-005: Быстрые модульные тесты на xUnit](decisions/ADR-005-unit-testing.md)
- [ADR-006: Упрощение архитектуры](decisions/ADR-006-architecture-simplification.md)
- [ADR-007: Инструменты управления приложением во внутрипроцессном MCP-сервере](decisions/ADR-007-in-process-app-tools.md)

## Статусы документов

Все перечисленные документы имеют статус `Accepted` и фиксируют решения, принятые до начала реализации. Версии NuGet-пакетов должны централизованно задаваться в `Directory.Packages.props`; при реализации выбирается последняя совместимая стабильная версия и фиксируется lock-файлом.

Фактическое состояние работ, результаты проверок и следующий инкремент фиксируются в документе [«Ход реализации»](11-implementation-progress.md). Он имеет статус `Active` и обновляется после каждого завершённого инкремента.

## Language convention

All UI text, source-code comments, identifiers and technical messages introduced by the project are written in English. Project documentation is maintained in Russian unless a document explicitly requires another language.

Текущие решения: [ADR-006](decisions/ADR-006-architecture-simplification.md).

- [Инструменты MCP по умолчанию](16-default-mcp-tools.md) — запуск процессов, разрешения и CLI.
