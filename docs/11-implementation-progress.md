# Ход реализации

Статус: Active

Этот документ является журналом фактически выполненных работ. Он обновляется после каждого завершённого инкремента вместе с соответствующими архитектурными и эксплуатационными документами.

## Правила ведения

- Фиксировать только реализованные и проверенные изменения.
- Для каждого инкремента указывать результат, затронутые этапы плана и выполненные проверки.
- Незавершённую функциональность явно отделять от готовой.
- Существенное изменение принятого решения сначала оформлять отдельным ADR.
- Не считать этап завершённым, пока не выполнены его exit criteria из плана реализации.

## Текущее состояние этапов

| Этап | Состояние | Выполнено | Осталось |
|---|---|---|---|
| 0. Foundation | В работе | Solution, проекты, `net10.0`, analyzers, central package management, Pure.DI, тестовые проекты | Общие clock/result contracts, CI |
| 1. Projects и storage | В работе | Project aggregate, versioned JSON project document, revision, atomic write/recovery, project and security settings CRUD | Separate documents/index, import/export and stronger recovery diagnostics |
| 2. Hosted WASM shell | В работе | Host, Web, same-origin WASM, Pure.DI composition roots, health endpoint, project and security settings UI/API | CSP, session and CSRF protection |
| 3–12 | Не начаты | — | Реализация согласно плану |

## Инкремент 001 — каркас приложения и доменная модель проектов

Дата: 2026-08-11

Состояние: завершён

### Реализовано

- Создано решение `AI.Client.slnx` и проекты Domain, Contracts, Application, Infrastructure, Host, Web, History MCP и FileSystem MCP.
- Настроены .NET 10, nullable reference types, latest recommended analyzers, warnings as errors и central package management.
- Подключены Pure.DI и Pure.DI.MS; для Host и Web созданы отдельные composition roots.
- Реализован Project aggregate с идентификатором, названием, временными метками, разрешёнными директориями и подключениями MCP-серверов.
- Реализованы отдельные политики `Allow`, `Ask`, `Deny` для каждого MCP-инструмента.
- Идентичность инструмента включает MCP server id и имя инструмента; schema hash участвует в политике и сбрасывает устаревшее разрешение при изменении схемы.
- Добавлены application-контракты репозитория и сценарий чтения проекта.
- Создан минимальный Hosted Blazor WebAssembly shell и gateway health endpoint `/api/health`.
- Настроена публикация и маршрутизация static web assets для Development и Production.
- Созданы заготовки отдельных процессов History MCP и FileSystem MCP. Реальные MCP-инструменты в этом инкременте ещё не реализованы.

### Тесты

- Используются только быстрые модульные тесты на xUnit v3, Shouldly и Moq.
- Добавлено 6 тестов Domain и 2 теста Application.
- Тесты не используют сеть, файловую систему, процессы или внешнее окружение.

### Проверки

| Проверка | Результат |
|---|---|
| `dotnet build AI.Client.slnx --nologo` | Успешно, 0 warnings, 0 errors |
| `dotnet test AI.Client.slnx --no-build --nologo` | Успешно, 8 из 8 тестов |
| Development: `/api/health` | HTTP 200 |
| Development: `/` и `/_framework/blazor.webassembly.js` | HTTP 200 |
| Published Production: `/api/health` | HTTP 200 |
| Published Production: `/` и `/_framework/blazor.webassembly.js` | HTTP 200 |

### Следующий инкремент

Локальное JSON-хранилище проектов:

- versioned JSON envelope;
- repository через интерфейс;
- optimistic concurrency по revision;
- атомарная запись и восстановление после незавершённой записи;
- абстракция файловой системы;
- быстрые модульные тесты без обращения к реальной файловой системе.

## Журнал изменений

| Дата | Изменение |
|---|---|
| 2026-08-11 | Создан журнал. Зафиксирован инкремент 001 и фактическое состояние этапов 0–2. |
| 2026-08-12 | Зафиксировано языковое правило: UI и комментарии в исходном коде ведутся на английском; существующие строки Web shell переведены. |
| 2026-08-12 | Добавлено build-приложение по подходу `dotnet-matrix/build`: Pure.DI composition root, интерфейсные targets и команды `build`, `test`, `verify`, `publish`; `verify` успешно выполнен. |
| 2026-08-12 | Добавлены versioned-конфигурации Rider для запуска Host, проверки и публикации. |
| 2026-08-12 | Тестовая стратегия дополнена стилем `CSharpInteractive.Tests/CISettingsTests.cs`; реализован JSON storage slice проектов с in-memory unit tests, optimistic concurrency, temporary-file recovery и Pure.DI registration в Host. |
| 2026-08-12 | Реализован CRUD метаданных проекта: Application service, same-origin `/api/projects`, revision conflicts, Web UI для списка, создания, выбора, редактирования и удаления; `verify` и локальная smoke-проверка API пройдены. |
| 2026-08-12 | Реализован CRUD security settings: directory grants, MCP bindings и tool policies передаются и сохраняются как единый revisioned document; UI поддерживает добавление, удаление и редактирование, а Application tests проверяют связность policy с MCP server. |
| 2026-08-12 | Добавлен Live chat preview для OpenAI-compatible `chat/completions`: Host adapter, временный API key без сохранения, Web form и unit tests mapping HTTP request/response. Локальная проверка невалидного запроса вернула ожидаемый HTTP 400. |
| 2026-08-12 | Исправлена WASM DI-конфигурация `HttpClient`: base address теперь берётся из `NavigationManager.BaseUri`, поэтому relative same-origin API requests допустимы в browser runtime. |
| 2026-08-12 | Добавлены endpoint profiles в составе проекта: имя, OpenAI-compatible base URL и model сохраняются в versioned project JSON. API key хранится отдельно от project document: в локальном файле, защищённом Windows DPAPI для текущего пользователя. Web UI позволяет создать профиль, задать или заменить ключ и выбрать профиль для live chat; сохранённый ключ разрешается только на Host gateway и не возвращается в WASM. Добавлены unit tests защищённого credential store на in-memory file system и тестовом protector. |
| 2026-08-12 | Реализована локальная история чатов в составе проекта: domain model дерева сообщений, JSON documents с revision, Host/Web API, создание и выбор чатов в UI. После live-chat completion сохраняется пара user/assistant; parent message определяет активную ветку контекста. Добавлены unit tests ветвления и JSON round-trip. |
| 2026-08-12 | Добавлен безопасный Markdown UI: Markdig рендерит сохранённый исходный Markdown, source HTML отключён, а итоговый HTML очищается HtmlSanitizer перед выводом через `MarkupString`. Обновлена документация плана и правил тестирования; Web build успешно выполнен без warnings/errors. |
| 2026-08-12 | Реализовано ветвление чатов: UI позволяет выбрать любое сообщение как branch point; последующая отправка сохраняет отдельную дочернюю user/assistant пару и сохраняет прежнее продолжение. OpenAI-compatible adapter получает только выбранный путь сообщений и новый запрос; добавлен unit test сериализации completion context. |
| 2026-08-12 | UI переработан по workspace-референсу: sidebar содержит проекты и вложенные чаты, центральная область показывает выбранную ветку и composer, правая панель содержит project settings и AI endpoints. Добавлены явные состояния пустого проекта/чата, подсказка причины недоступности Send, карточки endpoint profile и подтверждение сохранения. |
| 2026-08-12 | Исправлена доступность endpoint settings на узких окнах: рядом с endpoint selector всегда показана кнопка `Add endpoint` или `Configure`; она открывает settings panel как overlay, автоматически создаёт первую карточку endpoint и после сохранения закрывает панель. |
| 2026-08-12 | Исправлен transitional UI defect: прежний prototype screen оставался в DOM и становился видимым из-за CSS cascade (`.shell` переопределял `.legacy-shell`). Legacy screen теперь принудительно исключён из layout; пользователю показывается только workspace UI. |
| 2026-08-12 | После UX-сверки принят Codex-like desktop workspace baseline и создан `12-ux-decisions.md`. Решения разделены на ближайший workspace foundation и отложенные streaming/MCP/FileSystem этапы; визуальная простота Codex имеет приоритет над буквальным воспроизведением избыточных промежуточных диалогов. |
| 2026-08-12 | Начат workspace foundation: project inspector разделён на вкладки General/Endpoints/Security/MCP, endpoint editor переведён на master-detail, добавлены Save/Cancel и минимальный локальный Lucide-like SVG component для icon-only actions. Host/Web build выполнен в изолированный output без warnings/errors. |
| 2026-08-12 | Workspace foundation продолжен: старая prototype-разметка физически удалена, проект получил сохраняемый default endpoint, чат — собственный сохраняемый `EndpointProfileId`, а новый чат наследует проектный default. Добавлены resize-разделители панелей с сохранением layout в `localStorage`, клавиатурный контракт composer (`Enter`, `Shift+Enter`, IME-safe), очистка и возврат focus после отправки. Pure.DI получает `IJSRuntime` как внешнюю Blazor dependency; статические application services не добавлялись. |
| 2026-08-12 | Проверка workspace slice: Host/Web build — 0 warnings, 0 errors; Domain — 10/10, Application — 8/8, Infrastructure — 13/13. Новые unit tests проверяют инвариант default endpoint и JSON round-trip выбранного endpoint проекта и чата; тесты не используют сеть, процессы или реальную файловую систему. |
| 2026-08-12 | Повторный `dotnet run --project build -- verify` не завершился из-за запущенного пользователем `AI.Client.Host` (PID 53148), удерживающего DLL в стандартном `bin`. Процесс не останавливался. Для независимой проверки использованы отдельные `BaseOutputPath`: сборка и все 31 unit tests прошли успешно. |
| 2026-08-12 | Реализован следующий Codex-like UX slice: обе боковые панели сворачиваются и восстанавливаются, ширина и collapsed state сохраняются в `localStorage`; повреждённое layout-значение безопасно сбрасывается. Добавлен общий toast feedback для основных project/chat/endpoint/security операций. В endpoint master-detail добавлен `Test connection`: короткий `chat/completions` запрос проходит через тот же Host gateway и credential store, что и реальный чат, но не записывается в историю. Изолированная сборка прошла с 0 warnings/errors; Domain 10/10, Application 8/8, Infrastructure 13/13. |
| 2026-08-12 | Реализован OpenAI-compatible streaming: Infrastructure отправляет `stream: true`, отдельный SSE parser извлекает `choices[0].delta.content` до `[DONE]`, Host проксирует безопасные chunks в WASM. Composer отображает постепенный Markdown и кнопку Stop. User message сохраняется перед генерацией; завершённый, остановленный или оборванный assistant response сохраняется после неё, для последних двух выставляется `Incomplete`. JSON schema остаётся backward-compatible за счёт optional поля. Изолированная сборка — 0 warnings/errors; Domain 10/10, Application 8/8, Infrastructure 16/16. |
| 2026-08-12 | Завершён базовый chat management UX: New chat стал несохранённым draft до первой отправки; добавлены revision-safe Rename/Delete API и inline подтверждение удаления, Copy Markdown, Edit and branch и sibling branch switcher. Выбор sibling разворачивается до наиболее свежего leaf. UI actions используют локальные SVG icons и английские tooltip/aria-label. Изолированная сборка — 0 warnings/errors; Domain 12/12, Application 10/10, Infrastructure 16/16. |
| 2026-08-12 | Переработан chat workspace по UX feedback: устранены дублирующие collapse controls, compact sidebar сохраняет только Show sidebar, endpoint selector встроен в chat header, а draft больше не показывает `New chat` или тестовый текст. Composer получил исчезающий hint и явную Send button в своей нижней панели. Project settings стал устойчивой колонкой с закреплённой danger zone; Endpoints заменён на picker профиля и detail-форму. Изолированная Host/Web сборка прошла с 0 warnings/errors. |
| 2026-08-12 | По уточнённому UX решению удалён заголовок центральной chat области: имя чата остаётся в левой навигации, endpoint настраивается исключительно в правом inspector. Центральная область содержит только историю и composer. |
| 2026-08-12 | По следующему UX решению правая Project settings panel полностью удалена из workspace. У выбранного проекта добавлено меню `…`: Project settings открывает вкладочное modal dialog, Delete project находится в том же меню и требует inline подтверждения. Центральная часть остаётся пространством истории и composer. |
| 2026-08-12 | Размер Project settings modal стабилизирован: ширина и высота не зависят от активной вкладки, а на небольшом viewport прокручивается только внутреннее содержимое. |
| 2026-08-12 | Из Project settings modal удалена дублирующая кнопка `X`: Save сохраняет и закрывает, Cancel восстанавливает сохранённое состояние и закрывает; MCP без редактирования использует Close. |
| 2026-08-12 | Действия Project settings унифицированы: один закреплённый footer с `Save` и `Cancel` используется всеми вкладками. Специфичное для endpoint действие `Test connection` остаётся рядом с редактируемым профилем. |
| 2026-08-12 | Project settings увеличен до `42rem × 52rem` с ограничением по viewport, чтобы форма endpoint помещалась без лишней вертикальной прокрутки на стандартном экране. Кнопка добавления endpoint выровнена по высоте поля выбора профиля. |
| 2026-08-12 | Удаление endpoint перенесено к выбору профиля: компактная кнопка `−` расположена рядом с `+`, имеет tooltip и недоступна, когда профиль не выбран. Нижняя текстовая кнопка удаления убрана. |
| 2026-08-12 | Из sidebar удалён декоративный блок `AI.Client / Foundation`. Кнопки скрытия и восстановления sidebar получили одинаковую высоту управляющей строки и одинаковую вертикальную позицию в обоих состояниях. |
| 2026-08-12 | По визуальной проверке уточнено позиционирование sidebar toggle: кнопка восстановления закреплена абсолютно на том же верхнем отступе `0.45rem`, что и кнопка скрытия, и центрируется внутри свёрнутой колонки. |
| 2026-08-12 | Реализован lazy chat creation: пользователь может написать сообщение сразу после выбора или создания проекта, а чат создаётся при первой отправке и получает заголовок из текста сообщения. Пустой экран объясняет это поведение. Если endpoint не настроен, отправка больше не завершается молча — composer показывает конкретную ошибку. |
| 2026-08-12 | Действия выбранного проекта переведены на компактные SVG-кнопки: `+` создаёт новый чат, sliders открывает меню настроек. Глобальная текстовая кнопка `New chat` удалена. Send заменён на круглую светлую кнопку со стрелкой вверх. `Test connection` сокращён до `Test` с сохранением полного tooltip. |
| 2026-08-12 | Контекстные меню закрываются кликом вне меню, при смене проекта или чата и после выполнения команды. К строкам чатов добавлено меню с `Delete chat`, inline-подтверждением и optimistic revision check существующего History API. |
| 2026-08-12 | Улучшена видимость фокуса composer: textarea использует белую каретку и контрастный цвет текста, а контейнер получает заметную границу через `focus-within`. |
| 2026-08-12 | Все кнопки приведены к общей визуальной системе кнопки настроек: тёмный нейтральный фон, серая рамка, одинаковые hover, keyboard focus и disabled states. Icon-only, круглые и текстовые варианты сохраняют свою форму, опасные действия — приглушённый красный семантический акцент. |
| 2026-08-12 | Текстовая кнопка `Stop` в composer заменена круглой icon-only кнопкой с квадратным stop glyph, tooltip `Stop generation` и accessibility label. |
| 2026-08-12 | После аудита button states активная Send снова получила контрастный светлый фон и тёмную иконку; hover осветляет и слегка поднимает кнопку, active возвращает её на место, disabled использует отдельный тёмный вид без общей opacity. Для остальных кнопок добавлено единое active-состояние. |
| 2026-08-12 | Composer теперь синхронизирует текст по событию `input`, а не только по стандартному для `InputTextArea` событию `change`. Поэтому доступность Send пересчитывается во время набора, без потери фокуса textarea. |
| 2026-08-12 | Завершение OpenAI-compatible streaming больше не зависит исключительно от SSE marker `[DONE]`: parser завершает перечисление также при непустом стандартном `choices[0].finish_reason`. Это убирает зависание UI в `Assistant · Generating` на endpoint, которые сообщают `stop`, но удерживают HTTP stream открытым. Добавлен быстрый модульный тест. |
| 2026-08-12 | Role labels `Assistant` и `User` удалены из сообщений. Состояние генерации вынесено в отдельную live status строку непосредственно над composer и исчезает после завершения потока. Текст состояния продолжения изменён на `Continuing selected chat`; у частично сохранённого ответа остаётся только метка `Incomplete`. |
| 2026-08-12 | Устранён startup render failure после live-binding изменения composer: `InputTextArea` с конкурирующими `change` и ручным `input` handlers заменён на native `textarea` с единым `@bind:event="oninput"`. JS keyboard attachment теперь получает `ElementReference` напрямую. |
| 2026-08-12 | Добавлен chat autoscroll после завершения DOM render: при выборе чата, сохранении user message и каждом streaming chunk компонент ставит pending scroll, а `OnAfterRenderAsync` прокручивает history container к `scrollHeight`. |
| 2026-08-12 | Для OpenAI-compatible endpoint с некорректно незакрытым SSE добавлен fallback idle timeout: если после последнего события 10 секунд нет данных, parser завершает поток как полный ответ. Стандартные `[DONE]` и `finish_reason` остаются приоритетными; пользовательская Stop продолжает сохранять partial response как `Incomplete`. |
| 2026-08-12 | Исправлена семантика streaming idle timeout: deadline теперь отсчитывается от последнего содержательного token chunk. Пустые SSE-строки, comments и keep-alive больше не сбрасывают таймер и не могут бесконечно удерживать UI в состоянии `Generating...`. |
| 2026-08-12 | Кнопка контекстного меню чата переведена с glyph `…` на общую SVG-иконку sliders, используемую для настроек и контекстных действий. Tooltip и accessibility label `Chat menu` сохранены. |
| 2026-08-13 | Реализована глобальная конфигурация приложения: постоянные sidebar-разделы `Connections`, `Security`, `MCP` открываются в центральной области. Connections и MCP сохраняются в отдельных JSON; secrets остаются write-only на Host. Project settings сведён к одной странице с Name, Description, выбором Connection и project-only directory grants (`Read only`/`Read/write`, всегда recursive). MCP UI сохраняет Streamable HTTP и stdio definitions без подключения и tools discovery. Текущий Qwen вручную перенесён в глобальный default Connection с сохранением GUID существующих чатов. |
| 2026-08-13 | Исправлены регрессии после глобальной навигации: keyboard handler composer повторно подключается к новому textarea DOM element при возврате из глобального раздела, поэтому Enter снова отправляет сообщение. Project settings переведён на трёхстрочную grid-схему без удалённого tabs-row, получил content-sized высоту, меньшую ширину и компактный Description; footer больше не растягивает Save/Cancel. |
| 2026-08-13 | Composer автоматически увеличивает высоту до 14 строк. До достижения порога вертикальный scrollbar принудительно скрыт; после порога высота фиксируется и включается внутренняя прокрутка. Пересчёт выполняется при вводе, первоначальном подключении, фокусировке и программной подстановке текста. |
| 2026-08-13 | Исправлено падение WebAssembly при пустом или устаревшем static asset `chatComposer.js`: JS enhancement больше не является критической зависимостью первого рендера, ошибки import/attach перехватываются. Enter обрабатывается непосредственно Blazor, а CSS `field-sizing: content` сохраняет базовый auto-grow без JS. |
| 2026-08-13 | Для сообщений пользователя и ассистента добавлено действие `Fork from here` с отдельной SVG-иконкой. Оно выбирает сообщение как leaf новой ветви, очищает и фокусирует composer; следующая отправка создаёт дочернюю ветвь, не изменяя исходную историю. |
| 2026-08-12 | Добавлено постоянное структурированное логирование Host на `Microsoft.Extensions.Logging`: Console и ежедневные JSONL в `%LocalAppData%\AI.Client\logs`, retention 14 дней, streaming event IDs `1001–1005`, без prompt/token content и credentials. |
| 2026-08-12 | Добавлен `AI.Client.Cli` для headless LLM testing: отдельные многошаговые sessions, реальный Host SSE route, project endpoint/protected credential, JSON stdout, `session.json` и append-only `transcript.jsonl`. MCP/security snapshot фиксируется, но `agent` честно возвращает `not_supported` до реализации общего Agent Runtime. Build automation получила команду `chat`. |
| 2026-08-12 | Headless CLI расширен воспроизводимой проверкой Stop через `session send --cancel-after-ms`: отменяется тот же downstream HTTP streaming request, результат возвращается как `cancelled`, partial text фиксируется только в transcript и не загрязняет дальнейший session context. |
| 2026-08-12 | Реальный smoke-test выявил, что ASP.NET logging state может содержать несериализуемый `RuntimeMethodInfo`. JSONL provider сделан fail-safe: property values нормализуются в primitives/strings, а исключения provider не могут вернуть HTTP 500 приложению. |
| 2026-08-12 | Реальная проверка Qwen3-Coder-480B подтвердила end-to-end Stop: Host получил 135 chunks и записал `ChatStreamCancelled` через 2898 ms. В headless CLI найдено буферизующее поведение `PostAsJsonAsync`; streaming переведён на `SendAsync(..., ResponseHeadersRead)`, чтобы transcript видел chunks и partial response до отмены. |
| 2026-08-12 | По серверному логу UI-зависания установлено: Host штатно завершил 26 chunks за 850 ms, значит Web застревал после SSE при сохранении истории. Cleanup `_isSending`, cancellation source и streaming buffer вынесен во вложенный `finally`; ошибка append/reload истории больше не оставляет `Generating...` и Stop, а показывается отдельным сообщением. |
| 2026-08-12 | Повторная трассировка подтвердила полный успешный цикл Host + history (30 chunks, POST/GET 200), но UI не перерисовывался до завершения последнего JS focus interop. После сброса `_isSending` теперь немедленно вызывается `StateHasChanged`; восстановление focus выполняется вторично и `JSException` не влияет на completion state. |
# 2026-08-13: branch tree and inline rename

- Accepted sidebar design variant 2: root chats with nested branch conversations.
- Added persistent custom branch titles to chat JSON while keeping the existing parent-linked message tree.
- Added branch rename and cascading delete application/API operations.
- Added selected-chat branch rendering, SVG branch icons, capped visual indentation, and inline rename for projects, chats, and branches.
- Added fast domain and serializer unit tests for branch rename, cascade deletion, and JSON restoration.
- Fixed Blazor startup after rebuilding while the browser retained an older bootstrap module: conditional requests for `/_framework/dotnet.js` could be answered by mapped static assets with `200` and an empty body. The Host now forces a complete, non-cached response for this bootstrap file.
- Replaced the plain Blazor loading placeholder with a themed application splash screen, animated indeterminate progress, accessible loading semantics, and a reduced-motion fallback.
- Disabled browser caching for the application HTML shell as well as the WebAssembly bootstrap module, so splash and startup changes appear after a normal reload rather than requiring cache cleanup.
- Aligned directory path, access mode, add, and remove controls to the same 2.7rem height in Project settings.
- Added Escape keyboard handling to close Project settings through the same cancellation flow as the Cancel button.
- Replaced text removal controls in Connections and MCP with the shared trash SVG icon and positioned the SVG add action immediately below each settings list.
- Aligned the remove action directly above the selected settings card, at the card's right edge, to make its ownership clear.
- Moved the remove action onto the selected Connections/MCP list item itself; the add action remains immediately below the list.
- Moved the Connection `Enabled` and `Default` controls into one horizontal row at the top of the editor.
- Added an explicit compact fork mode: source-message highlight, fork status above the composer, icon-only cancel, branch-specific placeholder, and a disabled fork action at the active branch leaf.
- Fixed sidebar discovery of forks created from user messages: an alternative user message can be a sibling of the original assistant response, not only another user sibling.
- Replaced ambiguous numeric message branch links with a compact SVG `N branches` picker. Its menu shows `Original response`/`Original message`, content-derived alternative names, and a check icon for the active path; Escape closes the picker.
- Added the SVG `Edit and replace branch` action for user messages, a distinct replacement composer mode, cancellation without mutation, and replacement of the selected message subtree on send.
- Reworked the replace icon as a pencil inside a circular replacement arrow and applied the same destructive color language used by delete actions to both the message action and replacement status.
- Moved the `N branches` control onto the source message where the fork actually occurs. While a fork is waiting for its first message, the source shows a compact `New branch` indicator; after send it becomes the branch picker.
- Made branch presence permanently visible below the source message as `Branch N of M`, with an SVG branch icon and a short tree connector. Copy/Fork/Edit actions remain hover-only.
- Fixed branch switching in the chat sidebar: every sibling path at a fork, including the original path, is now rendered as a selectable branch. Previously only alternatives were shown, so returning to the original path was impossible after the root restored the last selected branch.
- Changed root chat selection to always open the original path; nested branch rows continue to open their corresponding alternative paths.
- Removed the now-redundant original-path branch row from the sidebar; the root chat represents that path and nested rows represent alternatives only.
# 2026-08-13: concurrent chat runs, Host foundation

- Added the accepted concurrent-runs architecture in `docs/14-concurrent-chat-runs.md`.
- Added per-chat `ChatRunState` with queue, streaming buffer, unread/error/status state, revision, and processed operation IDs.
- Added atomic `<chatId>.run.json` persistence beside each chat document; persisted generating runs restore as interrupted with partial text retained.
- Added the singleton Host dispatcher: independent worker and cancellation source per chat, sequential queue processing, history persistence, connection credential resolution, and state publication.
- Added HTTP enqueue/stop/read commands and an SSE endpoint that sends full snapshots on connect plus subsequent snapshots.
- Added domain tests for command idempotency and restart interruption behavior.
- The WebAssembly Send/Stop pipeline is intentionally still on the previous implementation until the next stage connects queue editing, per-chat streaming, sidebar statuses, and focus/read tracking together. This avoids exposing two competing run owners in the UI.

## Increment 014 - concurrent chat runs

Date: 2026-08-13

Status: completed

- Added independent Host-owned background execution for multiple chats.
- Added persisted per-chat run state, message queues, idempotent enqueue operations, SSE snapshots, stop/pause, resume, clear, edit, remove, and reorder commands.
- A Host restart converts an active generation to `Interrupted`, preserves partial content, and leaves its queue paused until explicit resume.
- Added chat and project status indicators, unread tracking, queue controls, background completion notifications, and automatic history refresh.
- Deleting a running chat now stops its generation and clears the queue before deleting history.
- Added fast Domain unit tests for queue idempotency, recovery, editing, ordering, removal, and resume behavior.

### Follow-up fix

- Chat listing now ignores persisted `*.run.json` state documents. Previously the chat repository treated them as chat-history documents and failed with `Chat ID cannot be empty`, which the Web UI incorrectly surfaced as an endpoint failure.
- Added a fast isolated repository test using xUnit, Shouldly, and Moq.
- Streaming run snapshots and JSON persistence are throttled to a 150 ms interval instead of executing for every token. The browser bridge also coalesces pending SSE snapshots, keeping project and chat navigation responsive during generation.
- The queue, composer status, and composer now occupy explicit rows in a single-column conversation grid. Showing the queue after send no longer creates an implicit second grid column or shifts the input area horizontally.
- A message is removed from `Queued` when processing starts, rather than after its response completes. Queue items support mouse drag-and-drop ordering in addition to keyboard-accessible move up/down actions.
- Runs and queues are now scoped by `(chatId, branchId)`. Branches in the sidebar show their own generating, unread, interrupted, or failed status and can execute concurrently; chat and project rows retain aggregate priority status.
- Message submission is guarded by a single-flight flag in both Blazor and the composer JavaScript bridge. Duplicate Enter events can no longer create two chats while the first asynchronous create request is still pending.
- Opening a branch marks the actual resolved branch run as read, including pending runs anchored to a fork source. The parent chat row now shows only the original branch status instead of duplicating child-branch progress; project status remains aggregate.
- Run snapshots are applied monotonically by revision. A delayed SSE snapshot can no longer restore an unread indicator after a newer `MarkRead` response has cleared it.
- Explicit chat or branch navigation always marks the selected run as read. It no longer depends on the browser focus flag, which may still be false during the click that activates a newly loaded application tab.
- Forking closes any open message branch picker. When the background run adds the new branch to history, the UI selects the matching child of the fork source instead of selecting the last message from the entire chat.
- Run command URLs omit `branchId` for the original branch instead of sending `branchId=`. Empty nullable GUID query values caused ASP.NET binding failures and HTTP 400 responses, preventing unread status from being cleared.
- While a newly forked branch is not yet present in chat history, its pending run is treated as the selected run via the saved fork anchor. Completion now reloads history and selects the newly created child path instead of leaving the original branch visible.
- After a pending branch becomes part of history, the UI records an exact `branch root ID -> run branch ID` alias. Nested branches no longer fall back to an ancestor run when clearing unread state or resolving status.
- Active branch resolution chooses the deepest matching branch for the current leaf. Parent branches are no longer selected simultaneously with a nested branch, and `MarkRead` targets the nested run.
- Sidebar selection is exclusive: the chat row is selected only for the original branch. A parent branch leaf stops before a nested branch root, so clicking a parent branch no longer immediately resolves back into its deepest child.
- Branch status resolution no longer guesses a run from the branch root's `ParentId`. That fallback assigned one pending fork run to every sibling or descendant branch sharing the same fork point. Only direct branch IDs and exact recorded aliases may render status beside a branch.
- Selecting an option in the message branch picker follows that option's earliest child path. `Original message` no longer falls through to the newest sibling branch.
- Active generation is represented by a static green status dot, consistent with the blue unread dot and other sidebar state indicators.
- Sidebar row status and action controls use a compact right-side layout with smaller fixed icon buttons, tighter spacing, and preserved tooltips and focus targets.
- Project, chat, and branch rows share fixed right-side columns for status, add, rename, and menu controls. Unused actions leave an empty column so controls align vertically across the entire tree.
- Replaced separate sidebar status dots with a bottom-edge status line that does not consume an action column: generating uses a smoothly flowing green indeterminate animation, unread is solid blue, failed is solid red, and interrupted or paused is gray dashed. Selected-row highlighting remains independent, status details remain available through the row tooltip, and reduced-motion environments receive a static green line. Right-side project, chat, and branch actions were compacted into aligned fixed columns.
- Increased the generating-line motion contrast: a bright green highlight now travels across a darker green track using an explicitly animated background position, making progress visible instead of reading as a uniformly glowing border.
- Changed generating progress to a physically translated highlight segment over a static dark-green track. The meaningful state animation is no longer disabled by the operating system's reduced-motion preference, which had made the indicator appear static on the target workstation.
- Corrected the status geometry after visual review: state is shown only on the row's lower gray boundary. During generation, a short green highlight travels along that boundary; unread, failed, and interrupted use a blue, red, or dashed gray lower edge.
- Fixed branch switching after a background response completes. Selecting a branch now refreshes chat history and resolves its current leaf again by stable branch root ID, so the newly persisted assistant response is displayed instead of disappearing behind a stale pre-generation leaf snapshot.
- Fixed false project-level unread status caused by branch identity transition. Once a permanent run exists for a branch root, the superseded temporary fork-anchor run is excluded from project aggregation instead of remaining as an unreachable unread duplicate.
- Unified original and derived chat execution around mandatory branch identity without changing the visual tree. The original branch uses `ChatId` as `BranchId`; a derived branch preallocates its first user-message ID and uses the same value as `BranchId`. Runtime contracts, URLs, run persistence, queues, and status APIs no longer accept a null branch. Temporary fork-anchor aliases were removed, and run files now consistently use `<chatId>.<branchId>.run.json`.
- Hardened startup after the mandatory-branch change. Run persistence schema is now version 3; schema 1/2 files, missing or empty branch IDs, incomplete arrays, and malformed JSON are treated as incompatible absent runtime state instead of being published to WebAssembly and crashing project loading. Existing history and settings files are left untouched; only stale run state is ignored.
- Added run lifecycle cleanup and graph reconciliation. Project and chat deletion remove their persisted and in-memory runs. After branch deletion, message append, replacement, or worker completion, valid runs are recalculated as `ChatId` plus current alternative user-message roots; obsolete files and runtimes are cancelled and removed. This also handles a node that ceases to be a branch root when its sibling is deleted.
- Cancelling project or chat deletion now closes the entire context menu instead of returning to its initial menu state.

## 2026-09-07 — встроенный MCP process_run

Реализованы stdio-сервер инструментов по умолчанию, schema validation, цикл tool calls в Chat Completions, подтверждения конкретного вызова, повторная проверка политик и сохранение пар вызов–результат. Web показывает вызовы и результаты; CLI поддерживает отдельное решение по approval. Сервер включён в build/publish, зависимости подключены через central package management.

Проверены настоящий MCP-вызов `dotnet --info`, продолжение генерации и восстановление истории после перезапуска Host с локальным тестовым endpoint. Визуальная проверка не выполнена: браузерный инструмент заблокировал локальный URL. Подробнее о границах и проверках: [инструменты по умолчанию](16-default-mcp-tools.md).

Итоговая проверка: `dotnet run --project build -- verify` — 80 тестов, сборка без предупреждений и ошибок. `publish --output artifacts/publish-mcp-final` завершён; сквозной сценарий повторён на опубликованном Host и поставляемом MCP-сервере.

## 2026-09-10 — FileSystem tools и fetch во встроенном MCP

Состав встроенного сервера доведён до 13 инструментов: к `process_run` добавлены `fetch` и одиннадцать FileSystem tools (`list_allowed_directories`, `read_text_file`, `read_multiple_files`, `list_directory`, `directory_tree`, `search_files`, `get_file_info`, `write_file`, `edit_file`, `create_directory`, `move_file`). Набор выбран по референсным серверам `modelcontextprotocol/servers`: `git` не дублируется, поскольку покрывается `process_run`; `memory`, `sequentialthinking` и `time` оставлены за границей встроенного набора.

Directory grants проекта впервые получили исполняемый смысл. `IToolSessionFactory.OpenAsync` принимает grants, `DefaultToolSessionFactory` передаёт их серверу через `AI_CLIENT_DIRECTORY_GRANTS`, а `PathGuard` на стороне сервера проверяет абсолютность пути, снимает `..`, разрешает reparse point по всей цепочке существующих компонентов, сверяет containment и требуемую capability. Отсутствие grants означает отказ, а не полный доступ. Host канонизирует path-аргументы до подтверждения, поэтому пользователь и сервер оценивают один и тот же путь.

Проверка: 93 теста в четырёх тестовых проектах, компиляция solution без предупреждений. Полный `verify` с копированием выходных файлов Host не выполнялся — запущенный экземпляр `AI.Client.Host` удерживал свои сборки.
