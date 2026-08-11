# План реализации

## Выполнено: локальная история чатов

- Чат принадлежит проекту и содержит дерево сообщений с явной ссылкой на родительское сообщение.
- Выбранная ветка формирует контекст от корня до leaf message; альтернативные ответы остаются отдельными ветками.
- Чаты хранятся локально в JSON в каталоге проекта с revision и atomic temporary-file replace.
- Добавлены same-origin API и минимальный UI: создание чата, выбор чата и сохранение пары user/assistant после live-chat completion.

Статус: Accepted

Фактическое выполнение этапов и результаты проверок ведутся в отдельном журнале [«Ход реализации»](11-implementation-progress.md). Этап считается завершённым только после выполнения всех указанных для него exit criteria.

Каждый этап завершается работающим vertical slice и проверяемыми exit criteria.

## Этап 0. Foundation

- Создать solution и проекты.
- Настроить `net10.0`, nullable, analyzers и central package management.
- Подключить Pure.DI/Pure.DI.MS.
- Ввести strongly typed UUID v7 IDs, clock и result/error contracts.
- Добавить test projects на xUnit с Shouldly и Moq.
- Добавить CI build и быстрый unit-test suite без внешних ресурсов.
- Использовать отдельное build-приложение по образцу `dotnet-matrix/build`: Pure.DI composition root, интерфейсный target на каждую операцию и CLI-команды для build, test, verify и publish.

Готово, когда solution собирается, composition roots проверяются, Domain не зависит от Infrastructure, а `dotnet run --project build -- verify` выполняет build и быстрый unit-test suite.

## Этап 1. Projects и локальное storage ядро

- Реализовать Project aggregate.
- Реализовать JSON envelopes, atomic writer и schema validation.
- Реализовать project CRUD.
- Добавить directory grants, MCP bindings и per-tool policies.
- Добавить recovery после незавершённой atomic write.

Готово, когда проект переживает перезапуск, конфликт revision обнаруживается, а secrets отсутствуют в JSON.

Текущий прогресс: project metadata и security settings CRUD, revision conflict и recovery реализованы. Добавлен временный live chat preview для проверки OpenAI-compatible endpoint без сохранения credentials. MCP configuration сохраняется локально, но transport ещё не запускается. Следующий этап — endpoint profiles и защищённое хранение credentials в Host.

## Этап 2. Hosted WASM shell

- Создать AI.Client.Host и AI.Client.Web.
- Host раздаёт WASM с same origin.
- Настроить Pure.DI composition roots по образцу Matrix.Web.
- Реализовать project list/settings UI.
- Добавить CSP, session и origin/CSRF protection.

Готово, когда один Host executable открывает UI и CRUD проектов работает через versioned API.

## Этап 3. Endpoints и credentials

- Реализовать credential store в Host.
- Реализовать endpoint profiles.
- Добавить OpenAI Responses adapter.
- Добавить Chat Completions fallback.
- Добавить безопасный connection/capability check.

Готово, когда ключ не появляется в WASM/network response/log, а два endpoint profiles можно переключать.

## Этап 4. Streaming Markdown chat

- Реализовать Chat и immutable MessageNode.
- Реализовать streaming AgentEvents.
- Добавить cancel, incomplete и failed states.
- Добавить Markdig и HtmlSanitizer.
- Сохранять endpoint/model snapshots и usage.

Готово, когда чат восстанавливается после перезапуска, Markdown безопасен, а отмена не создаёт завершённый ответ.

## Этап 5. Branching

- Реализовать refs и branch head updates.
- Fork от любого node.
- Редактирование через новый путь.
- Garbage detection для недостижимых nodes.
- Копирование чата между проектами с remap IDs.

Готово, когда общая история не дублируется, а конфликт двух head updates обнаруживается revision check.

## Этап 6. MCP connection manager

- Подключить официальный MCP C# SDK.
- Реализовать stdio и Streamable HTTP transports.
- Реализовать initialization, pagination и list changed.
- Ввести ToolIdentity, alias registry и schema hash.
- Добавить trust status и tool catalog UI.

Готово, когда два сервера с одинаковым tool name корректно различаются, а schema change сбрасывает approval.

## Этап 7. History MCP

- Вынести project/chat repositories в отдельный MCP server.
- Реализовать system tools и resources.
- Перевести Host repository adapters на MCP.
- Сохранить local cache только как восстанавливаемый кэш UI.

Готово, когда все проекты и история доступны через MCP, но history tools не попадают в model tool list.

## Этап 8. Agent loop

- Преобразовать MCP descriptors в provider tools.
- Реализовать validation, Allow/Ask/Deny и approvals.
- Исполнять calls и сохранять call/result IDs.
- Добавить limits, timeout, cancellation и retry rules.
- Добавить tool timeline.

Готово, когда модель выполняет несколько MCP-вызовов, остановка безопасна, а completed side effect не повторяется.

## Этап 9. FileSystem MCP

- Реализовать `read`, `write`, `edit`, `delete`, `list`, `search`.
- Добавить canonical roots и argument constraints.
- Защитить от traversal, symlink/junction escape и TOCTOU.
- Реализовать hash-based optimistic edit и atomic write.
- Добавить structured results и audit.

Готово, когда модульные security tests на fake filesystem не позволяют выйти за grants, а edit отвергает устаревший hash.

## Этап 10. HTTP MCP authorization

- Реализовать Protected Resource Metadata discovery.
- Добавить RFC 8414/OIDC discovery, PKCE S256 и Resource Indicators.
- Добавить progressive scopes и `insufficient_scope` handling.
- Защитить token storage и redaction.

Готово, когда удалённый MCP server подключается стандартным OAuth flow без broad scopes.

## Этап 11. Надёжность и UX

- Reconnect и interrupted run recovery.
- Context compaction.
- Поиск по проекту.
- Export/import с schema validation.
- Backup/restore.
- Diagnostics bundle без secrets.
- PWA assets для offline UI shell.

## Этап 12. Release readiness

- Полный threat-model review.
- Dependency и vulnerability scan.
- Полный быстрый unit-test suite, независимый от Windows и локальной среды.
- Self-contained publish Host.
- Документация установки, обновления и восстановления.

## Порядок разработки внутри этапа

1. Domain contract и tests.
2. Application use case.
3. Infrastructure adapter.
4. Pure.DI registration.
5. UI/API composition.
6. Security negative tests.
7. Документация и acceptance check.

## Выполнено: Markdown в чате

- Ответы и сохранённые сообщения отображаются как Markdown через Markdig с расширениями.
- Встроенный HTML Markdown отключён, а полученный HTML дополнительно очищается HtmlSanitizer перед передачей в `MarkupString`.
- В истории сохраняется исходный текст Markdown, поэтому повторный рендеринг не меняет данные чата.

## Выполнено: ветвление чатов

- Пользователь выбирает любую сохранённую точку через **Branch from this message**.
- Новая отправка создаёт дочернюю пару user/assistant и не меняет существующее продолжение.
- В OpenAI-compatible запрос передаётся только путь от корня до выбранной точки плюс новый user message.

## Выполнено: workspace UI

- Экран перестроен в трёхпанельный workspace по принятому референсу: проекты и чаты слева, активный диалог и composer в центре, настройки проекта и endpoint profiles справа.
- Основной сценарий теперь линейный: выбрать проект, создать чат, выбрать endpoint, написать сообщение.
- Endpoint profile редактируется как отдельная карточка с подписанными полями и явным статусом сохранения.
- Security и MCP отделены от ежедневного сценария чата и показаны как advanced settings.
