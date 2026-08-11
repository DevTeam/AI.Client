# Стратегия модульного тестирования

Статус: Accepted

## Markdown renderer

Markdown rendering is isolated behind `IMarkdownRenderer`. The implementation disables source HTML in Markdig and sanitizes generated output before it is rendered as `MarkupString`. Future renderer tests must stay pure and use only string input/output; they must not require a browser runtime.

## Обязательный стек

- xUnit — test framework и runner.
- Shouldly — все проверки результата и состояния.
- Moq — mocks интерфейсных зависимостей.

Другой assertion framework или mocking framework не добавляется без отдельного архитектурного решения.

## Основные правила

Автоматические тесты должны быть:

- модульными;
- быстрыми;
- детерминированными;
- независимыми друг от друга;
- независимыми от порядка запуска и parallelization;
- независимыми от сети, диска, browser, процессов, credentials, locale, timezone и текущего времени;
- воспроизводимыми локально и в CI без дополнительной конфигурации.

Не допускаются:

- интеграционные и end-to-end тесты;
- live OpenAI/MCP calls;
- запуск Kestrel, браузера или MCP executable;
- чтение и запись реальной файловой системы;
- `Thread.Sleep`, ожидание реального времени и случайные retry delays;
- зависимость от environment variables или user profile;
- общий mutable state между тестами.

Целевой ориентир: основная масса тестов выполняется за миллисекунды, а полный suite — за секунды.

## Структура теста

Используется стиль `Given`–`When`–`Then` из `CSharpInteractive.Tests/CISettingsTests.cs`. Один тест проверяет одно наблюдаемое поведение. Test class является `public`, зависимости создаются как поля через `Mock<T>`, а создание SUT выносится в instance-метод `CreateInstance`. Для набора граничных значений используется `[Theory]` и `[InlineData]`. Название начинается с `Should` и описывает наблюдаемое поведение:

```csharp
[Fact]
public async Task ShouldNotCallMcpServerWhenToolIsDenied()
{
    // Given
    var connection = new Mock<IMcpConnection>();
    var policy = new Mock<IToolPolicyEvaluator>();
    policy
        .Setup(i => i.EvaluateAsync(It.IsAny<ToolInvocation>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(PolicyDecision.Deny);

    var agent = CreateInstance(connection.Object, policy.Object);

    // When
    var result = await agent.RunAsync(CreateRequest(), CancellationToken.None);

    // Then
    result.Status.ShouldBe(AgentRunStatus.Completed);
    connection.Verify(
        i => i.CallToolAsync(It.IsAny<ToolCall>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

`CreateInstance` и object mothers/builders могут использоваться для уменьшения шума, но не должны скрывать значимые входные данные теста. Comments in test code are written in English.

## Test seams

Внешние эффекты доступны только через интерфейсы:

```text
IAIEndpoint
IMcpConnection
IFileSystem
IAtomicFileWriter
ICredentialStore
IClock
IIdGenerator
IDelay
IBrowserStorage
```

Domain tests не используют mocks. Application tests используют Moq для портов. Для чистых алгоритмов предпочтительнее простой fake/value object, если он понятнее mock setup.

## Domain tests

- Project invariants.
- Chat/branch invariants.
- Tool policy precedence.
- Copy/remap between projects.
- AgentRun state transitions.
- Построение пути root → branch head.
- Обнаружение недостижимых nodes как чистая операция над графом.

Domain tests создают только in-memory objects и не зависят от Infrastructure.

## Storage tests

Storage logic тестируется поверх `IFileSystem`/`IAtomicFileWriter` с in-memory fake или Moq:

- сериализация и десериализация schema versions;
- план атомарного node/ref update;
- сбой между записью node и перемещением ref;
- revision conflict;
- orphan detection;
- corrupted JSON и hash mismatch;
- решение о migration/rollback.

Реальные temporary directories и OS-specific atomic rename в автоматических тестах не используются. Тонкие platform adapters остаются минимальными и проверяются code review, статическим анализом и ручной приёмкой.

## Application tests

- Agent loop через mocked AI/MCP ports.
- `Deny`, `Ask` и `Allow`.
- Approval lifecycle.
- Cancellation в каждой state transition.
- Max iterations, calls и deadline через fake clock/delay.
- Retry разрешённого idempotent read.
- Запрет retry завершённого side effect.
- Fork context construction.
- Фильтрация системных MCP tools.
- Сброс policy при изменении schema hash.

Потоковые сценарии представляются заранее подготовленным `IAsyncEnumerable<AgentEvent>` без сети и реальных задержек.

## MCP tests

MCP orchestration тестируется через mock/fake transport, а не реальный сервер:

- capability negotiation result mapping;
- paginated `tools/list`;
- обработка `notifications/tools/list_changed`;
- одинаковые tool names разных servers;
- invalid input/output schema;
- alias mapping;
- structured и unstructured results;
- OAuth challenge decision как чистое преобразование response metadata.

Запуск `stdio` process и Streamable HTTP server в test suite запрещён.

## FileSystem security tests

Path и policy logic должны быть отделены от `System.IO` и проверяться модульно:

- `..` traversal;
- absolute path outside root;
- symlink/junction metadata, возвращённые fake filesystem;
- case/canonicalization mismatch;
- UNC path;
- alternate data streams на Windows;
- delete при разрешённом только edit;
- oversized input/result;
- stale edit hash;
- excluded glob;
- широкая root directory без подтверждения;
- повторная policy check перед commit.

Каждый тест явно задаёт platform/path semantics через mock или value object, поэтому результат не зависит от ОС, где запущен runner.

## Provider adapter tests

Adapters получают заранее подготовленные JSON/SSE fixtures через mocked `HttpMessageHandler` или собственный transport interface:

- Responses API event mapping;
- Chat Completions fallback mapping;
- streaming frame fragmentation;
- несколько function calls;
- сохранение call ID и result;
- provider error/rate limit mapping;
- continuation fallback к полному локальному контексту.

Live API tests отсутствуют.

## UI tests

Razor presentation logic по возможности выносится в обычные view models/presenters и тестируется xUnit. Для изолированной проверки Razor component допускается bUnit как unit-level renderer, при этом:

- все services предоставляются через Moq;
- `IJSRuntime` заменяется mock;
- browser и Host не запускаются;
- HTTP и реальные timers не используются;
- assertions выполняются через Shouldly.

Playwright и другие end-to-end инструменты в test suite не используются.

## Детерминизм

- `IClock` возвращает фиксированное время.
- `IIdGenerator` возвращает заранее заданные IDs.
- `IDelay` завершается немедленно и фиксирует запрос задержки.
- Cancellation инициируется тестом в точной state transition.
- Culture и path comparison передаются явно.
- Коллекции сравниваются без предположения о порядке, если порядок не является контрактом.

## Moq verification

Проверять следует значимое взаимодействие на границе модуля: вызван ли MCP tool, сохранён ли node, запрошено ли approval. Не следует проверять каждую внутреннюю операцию или порядок вызовов, если порядок не является частью поведения.

`VerifyNoOtherCalls` применяется выборочно: чрезмерная проверка делает тесты хрупкими при безопасном рефакторинге.

## Quality gates

- Все тесты используют xUnit.
- Все assertions используют Shouldly.
- Interface mocks создаются через Moq.
- В test projects отсутствуют network/process/browser test fixtures.
- Тесты не требуют credentials или environment configuration.
- Повторный запуск даёт тот же результат.
- Полный suite остаётся быстрым; заметное замедление рассматривается как regression.
- Build и test проходят одинаково локально и в CI.
