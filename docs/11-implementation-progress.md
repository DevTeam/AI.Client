# Implementation progress

Status: Active

This document is the log of actually completed work. It is updated after each finished increment together with the corresponding architecture and operations documents.

## Open TODOs

Cross-cutting items that are not yet increments but must be picked up before they become silent behaviour gaps.

- **Surface automatic LLM context fallback to the user.** Today the planner silently replaces old
  turns with an LLM summary when deterministic compaction does not fit; the chat feed, run status
  and persisted history show nothing. Three layered options are documented in
  `21-tool-selection-and-adaptive-compaction.md` ("Making the fallback visible to the user"):
  log + run journal first, transport-side notification second, chat feed item last. Until at
  least the first option is implemented, neither users nor post-mortem log readers can tell that
  a compaction happened, which makes the recovery invisible.

## Recording rules

- Record only implemented and verified changes.
- For each increment, state the result, the affected plan stages, and the performed checks.
- Clearly separate unfinished functionality from finished.
- A significant change to an adopted decision is first formalized as a separate ADR.
- Do not consider a stage complete until its exit criteria from the implementation plan are satisfied.

## Current state of stages

| Stage | State | Done | Remaining |
|---|---|---|---|
| 0. Foundation | In progress | Solution, projects, `net10.0`, analyzers, central package management, Pure.DI, test projects | Shared clock/result contracts, CI |
| 1. Projects and storage | In progress | Project aggregate, versioned JSON project document, revision, atomic write/recovery, project and security settings CRUD | Separate documents/index, import/export and stronger recovery diagnostics |
| 2. Hosted WASM shell | In progress | Host, Web, same-origin WASM, Pure.DI composition roots, health endpoint, project and security settings UI/API | CSP, session and CSRF protection |
| 3–12 | Not started | — | Implementation per the plan |

## Increment 001 — application skeleton and project domain model

Date: 2026-08-11

State: completed

### Implemented

- Created the `AI.slnx` solution and the Domain, Contracts, Application, Infrastructure, Host, Web, History MCP, and FileSystem MCP projects.
- Configured .NET 10, nullable reference types, latest recommended analyzers, warnings as errors, and central package management.
- Connected Pure.DI and Pure.DI.MS; separate composition roots were created for Host and Web.
- Implemented the Project aggregate with ID, name, timestamps, allowed directories, and MCP server bindings.
- Implemented separate `Allow`, `Ask`, `Deny` policies for each MCP tool.
- Tool identity includes the MCP server ID and the tool name; the schema hash participates in the policy and invalidates a stale approval when the schema changes.
- Added application repository contracts and the project read scenario.
- Created a minimal Hosted Blazor WebAssembly shell and gateway health endpoint `/api/health`.
- Configured publishing and routing of static web assets for Development and Production.
- Created stubs for separate History MCP and FileSystem MCP processes. The real MCP tools have not been implemented in this increment.

### Tests

- Only fast unit tests on xUnit v3, Shouldly, and Moq are used.
- Added 6 Domain tests and 2 Application tests.
- The tests use no network, file system, processes, or external environment.

### Checks

| Check | Result |
|---|---|
| `dotnet build AI.slnx --nologo` | Success, 0 warnings, 0 errors |
| `dotnet test AI.slnx --no-build --nologo` | Success, 8 of 8 tests |
| Development: `/api/health` | HTTP 200 |
| Development: `/` and `/_framework/blazor.webassembly.js` | HTTP 200 |
| Published Production: `/api/health` | HTTP 200 |
| Published Production: `/` and `/_framework/blazor.webassembly.js` | HTTP 200 |

### Next increment

Local JSON project storage:

- versioned JSON envelope;
- repository through an interface;
- optimistic concurrency by revision;
- atomic write and recovery after an unfinished write;
- file system abstraction;
- fast unit tests without touching the real file system.

## Change log

| Date | Change |
|---|---|
| 2026-08-11 | Log created. Increment 001 and the actual state of stages 0–2 are recorded. |
| 2026-08-12 | Recorded the language rule: UI and source code comments are kept in English; existing Web shell strings were translated. |
| 2026-08-12 | Added the build application following the `dotnet-matrix/build` approach: Pure.DI composition root, interface targets, and `build`, `test`, `verify`, `publish` commands; `verify` ran successfully. |
| 2026-08-12 | Added versioned Rider configurations to run Host, verify, and publish. |
| 2026-08-12 | Test strategy extended with the `CSharpInteractive.Tests/CISettingsTests.cs` style; implemented the project JSON storage slice with in-memory unit tests, optimistic concurrency, temporary-file recovery, and Pure.DI registration in Host. |
| 2026-08-12 | Implemented project metadata CRUD: Application service, same-origin `/api/projects`, revision conflicts, Web UI for list, create, select, edit, and delete; `verify` and local API smoke checks passed. |
| 2026-08-12 | Implemented security settings CRUD: directory grants, MCP bindings, and tool policies are passed and saved as a single revisioned document; the UI supports add, remove, and edit, and Application tests verify policy-to-MCP-server consistency. |
| 2026-08-12 | Added the Live chat preview for OpenAI-compatible `chat/completions`: Host adapter, temporary unsaved API key, Web form, and unit tests for HTTP request/response mapping. A local check of an invalid request returned the expected HTTP 400. |
| 2026-08-12 | Fixed the WASM DI configuration of `HttpClient`: the base address is now taken from `NavigationManager.BaseUri`, so relative same-origin API requests work in the browser runtime. |
| 2026-08-12 | Added endpoint profiles inside the project: name, OpenAI-compatible base URL, and model are saved in the versioned project JSON. The API key is stored separately from the project document: in a local file protected by Windows DPAPI for the current user. The Web UI lets you create a profile, set or replace the key, and select the profile for live chat; the saved key is resolved only on the Host gateway and is never returned to WASM. Added unit tests of the protected credential store against an in-memory file system and a test protector. |
| 2026-08-12 | Implemented local chat history inside the project: message tree domain model, JSON documents with revision, Host/Web API, and chat create/select in the UI. After a live-chat completion a user/assistant pair is saved; the parent message defines the active context branch. Added branching unit tests and JSON round-trip tests. |
| 2026-08-12 | Added safe Markdown UI: Markdig renders the saved raw Markdown, source HTML is disabled, and the resulting HTML is sanitized by HtmlSanitizer before being emitted through `MarkupString`. Updated the plan and testing-rules documentation; the Web build succeeded with no warnings or errors. |
| 2026-08-12 | Реализовано ветвление чатов: UI позволяет выбрать любое сообщение как branch point; последующая отправка сохраняет отдельную дочернюю user/assistant пару и сохраняет прежнее продолжение. OpenAI-compatible adapter получает только выбранный путь сообщений и новый запрос; добавлен unit test сериализации completion context. |
| 2026-08-12 | UI was reworked using the workspace reference: the sidebar contains projects and nested chats, the central area shows the selected branch and composer, and the right panel holds project settings and AI endpoints. Added explicit empty-project/empty-chat states, a tooltip explaining why Send is unavailable, endpoint profile cards, and a save confirmation. |
| 2026-08-12 | Fixed endpoint settings accessibility on narrow windows: next to the endpoint selector an `Add endpoint` or `Configure` button is always shown; it opens the settings panel as an overlay, automatically creates the first endpoint card, and closes the panel after saving. |
| 2026-08-12 | Fixed a transitional UI defect: the previous prototype screen remained in the DOM and became visible due to a CSS cascade (`.shell` overrode `.legacy-shell`). The legacy screen is now forcibly excluded from the layout; only the workspace UI is shown to the user. |
| 2026-08-12 | После UX-сверки принят Codex-like desktop workspace baseline и создан `12-ux-decisions.md`. Решения разделены на ближайший workspace foundation и отложенные streaming/MCP/FileSystem этапы; визуальная простота Codex имеет приоритет над буквальным воспроизведением избыточных промежуточных диалогов. |
| 2026-08-12 | Начат workspace foundation: project inspector разделён на вкладки General/Endpoints/Security/MCP, endpoint editor переведён на master-detail, добавлены Save/Cancel и минимальный локальный Lucide-like SVG component для icon-only actions. Host/Web build выполнен в изолированный output без warnings/errors. |
| 2026-08-12 | Workspace foundation продолжен: старая prototype-разметка физически удалена, проект получил сохраняемый default endpoint, чат — собственный сохраняемый `EndpointProfileId`, а новый чат наследует проектный default. Добавлены resize-разделители панелей с сохранением layout в `localStorage`, клавиатурный контракт composer (`Enter`, `Shift+Enter`, IME-safe), очистка и возврат focus после отправки. Pure.DI получает `IJSRuntime` как внешнюю Blazor dependency; статические application services не добавлялись. |
| 2026-08-12 | Проверка workspace slice: Host/Web build — 0 warnings, 0 errors; Domain — 10/10, Application — 8/8, Infrastructure — 13/13. Новые unit tests проверяют инвариант default endpoint и JSON round-trip выбранного endpoint проекта и чата; тесты не используют сеть, процессы или реальную файловую систему. |
| 2026-08-12 | Повторный `dotnet run --project build -- verify` не завершился из-за запущенного пользователем `AI.Host` (PID 53148), удерживающего DLL в стандартном `bin`. Процесс не останавливался. Для независимой проверки использованы отдельные `BaseOutputPath`: сборка и все 31 unit tests прошли успешно. |
| 2026-08-12 | Реализован следующий Codex-like UX slice: обе боковые панели сворачиваются и восстанавливаются, ширина и collapsed state сохраняются в `localStorage`; повреждённое layout-значение безопасно сбрасывается. Добавлен общий toast feedback для основных project/chat/endpoint/security операций. В endpoint master-detail добавлен `Test connection`: короткий `chat/completions` запрос проходит через тот же Host gateway и credential store, что и реальный чат, но не записывается в историю. Изолированная сборка прошла с 0 warnings/errors; Domain 10/10, Application 8/8, Infrastructure 13/13. |
| 2026-08-12 | Реализован OpenAI-compatible streaming: Infrastructure отправляет `stream: true`, отдельный SSE parser извлекает `choices[0].delta.content` до `[DONE]`, Host проксирует безопасные chunks в WASM. Composer отображает постепенный Markdown и кнопку Stop. User message сохраняется перед генерацией; завершённый, остановленный или оборванный assistant response сохраняется после неё, для последних двух выставляется `Incomplete`. JSON schema остаётся backward-compatible за счёт optional поля. Изолированная сборка — 0 warnings/errors; Domain 10/10, Application 8/8, Infrastructure 16/16. |
| 2026-08-12 | Завершён базовый chat management UX: New chat стал несохранённым draft до первой отправки; добавлены revision-safe Rename/Delete API и inline подтверждение удаления, Copy Markdown, Edit and branch и sibling branch switcher. Выбор sibling разворачивается до наиболее свежего leaf. UI actions используют локальные SVG icons и английские tooltip/aria-label. Изолированная сборка — 0 warnings/errors; Domain 12/12, Application 10/10, Infrastructure 16/16. |
| 2026-08-12 | Переработан chat workspace по UX feedback: устранены дублирующие collapse controls, compact sidebar сохраняет только Show sidebar, endpoint selector встроен в chat header, а draft больше не показывает `New chat` или тестовый текст. Composer получил исчезающий hint и явную Send button в своей нижней панели. Project settings стал устойчивой колонкой с закреплённой danger zone; Endpoints заменён на picker профиля и detail-форму. Изолированная Host/Web сборка прошла с 0 warnings/errors. |
| 2026-08-12 | Per a refined UX decision the central chat area title was removed: the chat name stays in the left navigation, and the endpoint is configured exclusively in the right inspector. The central area contains only history and composer. |
| 2026-08-12 | Per the next UX decision the right Project settings panel was completely removed from the workspace. The selected project got a `…` menu: Project settings opens a tabbed modal dialog, Delete project lives in the same menu and requires inline confirmation. The central part remains the space for history and composer. |
| 2026-08-12 | Project settings modal size was stabilized: width and height do not depend on the active tab, and on a small viewport only the inner content scrolls. |
| 2026-08-12 | The duplicate `X` button was removed from the Project settings modal: Save saves and closes, Cancel restores the saved state and closes; MCP without editing uses Close. |
| 2026-08-12 | Project settings actions were unified: a single fixed footer with `Save` and `Cancel` is used by all tabs. The endpoint-specific `Test connection` action stays next to the editable profile. |
| 2026-08-12 | Project settings was enlarged to `42rem × 52rem` with viewport clamping, so the endpoint form fits without unnecessary vertical scrolling on a standard screen. The add-endpoint button is aligned in height with the profile selector. |
| 2026-08-12 | Endpoint removal was moved next to the profile selector: a compact `−` button sits beside `+`, has a tooltip, and is disabled when no profile is selected. The bottom text remove button was removed. |
| 2026-08-12 | The decorative `AI / Foundation` block was removed from the sidebar. The sidebar hide/restore buttons got the same control row height and the same vertical position in both states. |
| 2026-08-12 | After a visual review the sidebar toggle positioning was refined: the restore button is positioned absolutely with the same top offset `0.45rem` as the hide button, and is centered inside the collapsed column. |
| 2026-08-12 | Lazy chat creation was implemented: the user can type a message right after selecting or creating a project, and the chat is created on first send and gets its title from the message text. The empty screen explains this behavior. If no endpoint is configured, sending no longer fails silently — the composer shows a specific error. |
| 2026-08-12 | The actions of the selected project were moved to compact SVG buttons: `+` creates a new chat, sliders opens the settings menu. The global text button `New chat` was removed. Send was replaced with a round light button with an up arrow. `Test connection` was shortened to `Test` while preserving the full tooltip. |
| 2026-08-12 | Context menus close on click outside the menu, when switching project or chat, and after the command runs. Chat rows got a menu with `Delete chat`, inline confirmation, and an optimistic revision check against the existing History API. |
| 2026-08-12 | Improved composer focus visibility: the textarea uses a white caret and a high-contrast text color, while the container gets a noticeable border via `focus-within`. |
| 2026-08-12 | All buttons were aligned to the shared settings-button visual system: dark neutral background, gray border, identical hover, keyboard focus, and disabled states. Icon-only, round, and text variants keep their shape; dangerous actions use a muted red semantic accent. |
| 2026-08-12 | The text `Stop` button in the composer was replaced with a round icon-only button with a square stop glyph, the tooltip `Stop generation`, and an accessibility label. |
| 2026-08-12 | After a button states audit the active Send again received a contrasting light background and a dark icon; hover lightens and slightly raises the button, active puts it back in place, disabled usesьзует отдельный тёмный вид без общей opacity. Для остальных кнопок добавлено единое active-состояние. |
| 2026-08-12 | Composer теперь синхронизирует текст по событию `input`, а не только по стандартному для `InputTextArea` событию `change`. Поэтому доступность Send пересчитывается во время набора, без потери фокуса textarea. |
| 2026-08-12 | Завершение OpenAI-compatible streaming больше не зависит исключительно от SSE marker `[DONE]`: parser завершает перечисление также при непустом стандартном `choices[0].finish_reason`. Это убирает зависание UI в `Assistant · Generating` на endpoint, которые сообщают `stop`, но удерживают HTTP stream открытым. Добавлен быстрый модульный тест. |
| 2026-08-12 | Role labels `Assistant` и `User` удалены из сообщений. Состояние генерации вынесено в отдельную live status строку непосредственно над composer и исчезает после завершения потока. Текст состояния продолжения изменён на `Continuing selected chat`; у частично сохранённого ответа остаётся только метка `Incomplete`. |
| 2026-08-12 | Устранён startup render failure после live-binding изменения composer: `InputTextArea` с конкурирующими `change` и ручным `input` handlers заменён на native `textarea` с единым `@bind:event="oninput"`. JS keyboard attachment теперь получает `ElementReference` напрямую. |
| 2026-08-12 | Added chat autoscroll after DOM render completes: when selecting a chat, saving a user message, and on every streaming chunk the component sets a pending scroll, and `OnAfterRenderAsync` scrolls the history container to `scrollHeight`. |
| 2026-08-12 | For an OpenAI-compatible endpoint with an improperly closed SSE a fallback idle timeout was added: if 10 seconds pass after the last event without data, the parser finishes the stream as a complete response. Standard `[DONE]` and `finish_reason` remain priority; the user-initiated Stop continues to save the partial response as `Incomplete`. |
| 2026-08-12 | Fixed the semantics of the streaming idle timeout: the deadline is now counted from the last meaningful token chunk. Empty SSE lines, comments, and keep-alive no longer reset the timer and cannot keep the UI in the `Generating...` state indefinitely. |
| 2026-08-12 | The chat context menu button was changed from the `…` glyph to the shared sliders SVG icon used for settings and contextual actions. The tooltip and accessibility label `Chat menu` were preserved. |
| 2026-08-13 | Implemented global application configuration: persistent sidebar sections `Connections`, `Security`, `MCP` open in the central area. Connections and MCP are saved in separate JSONs; secrets stay write-only on Host. Project settings was reduced to a single page with Name, Description, Connection selection, and project-only directory grants (`Read only`/`Read/write`, всегда recursive). MCP UI сохраняет Streamable HTTP и stdio definitions без подключения и tools discovery. Текущий Qwen вручную перенесён в глобальный default Connection с сохранением GUID существующих чатов. |
| 2026-08-13 | Fixed regressions after global navigation: the composer's keyboard handler re-attaches to the new textarea DOM element when returning from a global section, so Enter again sends a message. Project settings was moved to a three-row grid layout without the removed tabs row, got a content-sized height, smaller width, and compact Description; the footer no longer stretches Save/Cancel. |
| 2026-08-13 | The composer auto-grows up to 14 rows. Before reaching the threshold the vertical scrollbar is forcibly hidden; after the threshold the height is fixed and inner scrolling is enabled. Recalculation runs on input, initial attach, focus, and programmatic text insertion. |
| 2026-08-13 | Fixed a WebAssembly crash on an empty or stale `chatComposer.js` static asset: JS enhancement is no longer a critical dependency of the first render, and import/attach errors are caught. Enter is handled directly by Blazor, while CSS `field-sizing: content` preserves the basic auto-grow without JS. |
| 2026-08-13 | For user and assistant messages the `Fork from here` action was added with a separate SVG icon. It selects the message as the leaf of a new branch, clears and focuses the composer; the next send creates a child branch without modifying the original history. |
| 2026-08-12 | Добавлено постоянное структурированное логирование Host на `Microsoft.Extensions.Logging`: Console и ежедневные JSONL в `%LocalAppData%\AI\logs`, retention 14 дней, streaming event IDs `1001–1005`, без prompt/token content и credentials. |
| 2026-08-12 | Added `AI.Cli` for headless LLM testing: separate multi-step sessions, real Host SSE route, project endpoint/protected credential, JSON stdout, `session.json`, and append-only `transcript.jsonl`. MCP/security snapshot is recorded, but `agent` honestly returns `not_supported` until the general Agent Runtime is implemented. Build automation received the `chat` command. |
| 2026-08-12 | The headless CLI was extended with a reproducible Stop check via `session send --cancel-after-ms`: the same downstream HTTP streaming request is cancelled, the result is returned as `cancelled`, and partial text is recorded only in the transcript without polluting further session context. |
| 2026-08-12 | A real smoke test revealed that the ASP.NET logging state can contain a non-serializable `RuntimeMethodInfo`. The JSONL provider was made fail-safe: property values are normalized to primitives/strings, and provider exceptions cannot return HTTP 500 to the application. |
| 2026-08-12 | A real check with Qwen3-Coder-480B confirmed end-to-end Stop: Host received 135 chunks and recorded `ChatStreamCancelled` after 2898 ms. Buffering behavior of `PostAsJsonAsync` was found in the headless CLI; streaming was switched to `SendAsync(..., ResponseHeadersRead)` so the transcript sees chunks and the partial response before cancellation. |
| 2026-08-12 | По серверному логу UI-зависания установлено: Host штатно завершил 26 chunks за 850 ms, значит Web застревал после SSE при сохранении истории. Cleanup `_isSending`, cancellation source и streaming buffer вынесен во вложенный `finally`; ошибка append/reload истории больше не оставляет `Generating...` и Stop, а показывается отдельным сообщением. |
| 2026-08-12 | A repeat trace confirmed a fully successful Host + history cycle (30 chunks, POST/GET 200), but the UI did not redraw until the last JS focus interop finished. After resetting `_isSending`, `StateHasChanged` is now called immediately; focus restoration runs as a secondary step and `JSException` does not affect the completion state. |
| 2026-09-15 | A live run of "check all connections with subtasks" showed that the policy timeout measured the call duration: a fan of eight subtasks died at the 120th second along with all started work, and the slots it occupied prevented any retry. The timeout was switched to count silence — a progress notification extends patience, as does an MCP resolution — with a hard ceiling of 30 minutes per call. |
| 2026-09-15 | An empty endpoint response (no text, no calls) killed the whole run: project setup broke off in the middle and the project was left half-finished. Nothing is persisted for such a turn, so the retry sends exactly the same request and cannot duplicate any side effect — the turn retries up to two times with 1- and 2-second delays, and only then the run fails. |
| 2026-09-15 | A file system failure reached the user as "UnauthorizedAccessException: Access to the path is denied" with a stack trace but no path: .NET does not put the file name into this message, and there was no way to tell which directory was missing permissions. `PhysicalTextFileSystem` now namesт путь при записи, переименовании и удалении, сохраняя тип исключения — по нему диспетчер классифицирует сбой как `Storage`. |
| 2026-09-15 | Названный путь сразу показал, что дело не в правах: отказ приходил на файле чата в собственном каталоге данных. Сохранение завершается переименованием временного файла поверх хранимого, а Windows отказывает переименовать файл, который кто-то держит открытым, — общий доступ на чтение не спасает. Четыре параллельные подзадачи, читающие чат, в который пишет их родитель, воспроизводили это надёжно. Замена идёт через `File.Replace` — операцию платформы ровно для этого случая, — а чтение открывает файл с `FileShare.ReadWrite | Delete`. |
| 2026-09-15 | Live load showed that `File.Replace` shifts the failure from the writer to the readers: while the document is being swapped, it is unopenable for a fraction of a millisecond and even does not exist, so the reader gets "file in use" or a 404. The read retries up to five times with 1–4 ms delays, and only a failure that survives them all is reported; a missing file is read as missing after the same budget. Verified: 7326 parallel reads against 250 writes of a single file — zero errors. |
| 2026-09-16 | The "for subtasks" flag became multi-valued. The "only one connection" restriction was removed; untargeted tasks are distributed round-robin among marked connections, so a fan of subtasks is split between providers instead of queuing at a single one. The queue is counted only for tasks without an explicit `connectionId`. |
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

## 2026-09-07 — built-in MCP process_run

Implemented the stdio server of default tools, schema validation, the tool calls loop in Chat Completions, per-call confirmations, policy re-checks, and persistence of call–result pairs. The Web shows calls and results; the CLI supports a separate approval decision. The server is included in build/publish; dependencies are wired through central package management.

A real MCP call of `dotnet --info` was checked, as well as generation continuation and history recovery after restarting Host with the local test endpoint. Visual check was not performed: the browser tool blocked the local URL. See [default tools](16-default-mcp-tools.md) for boundaries and verifications.

Final check: `dotnet run --project build -- verify` — 80 tests, build without warnings or errors. `publish --output artifacts/publish-mcp-final` finished; the end-to-end scenario was repeated on the published Host and the bundled MCP server.

## 2026-09-10 — FileSystem tools and fetch in the built-in MCP

The built-in server now ships 13 tools: in addition to `process_run`, it includes `fetch` and eleven FileSystem tools (`list_allowed_directories`, `read_text_file`, `read_multiple_files`, `list_directory`, `directory_tree`, `search_files`, `get_file_info`, `write_file`, `edit_file`, `create_directory`, `move_file`). The set is selected based on the `modelcontextprotocol/servers` reference servers: `git` is not duplicated because it is already covered by `process_run`; `memory`, `sequentialthinking`, and `time` are left outside the built-in set.

The project's directory grants have gained executable meaning for the first time. `IToolSessionFactory.OpenAsync` accepts grants; `DefaultToolSessionFactory` passes them to the server via `AI_CLIENT_DIRECTORY_GRANTS`; the server-side `PathGuard` checks path absoluteness, strips `..`, resolves reparse points across the entire chain of existing components, verifies containment, and enforces the required capability. No grants mean denial, not full access. Host canonicalizes path arguments before confirmation, so user and server evaluate the same path.

Check: 93 tests in four test projects, solution compilation without warnings. A full `verify` that copies Host output files was not run — the running `AI.Host` instance was holding its own assemblies.

## 2026-09-17 — file and directory deletion in the built-in MCP

The built-in server now ships 16 tools: in addition to the previous ones, `delete_file` and `delete_directory` were added. Until now, deletion was the only capability grant with no corresponding tool: `delete` was already issued together with `Read/write`, and `move_file` already required it for its source, but there was no way to actually delete a file.

The tools are split by path kind. `delete_file` deletes only a file, `delete_directory` only a directory; each refuses paths of the other kind and names the appropriate tool instead of guessing. `delete_directory` without `recursive` removes only an empty directory; for a non-empty one it returns an error that points to the flag — a call that did not request recursion cannot remove more than the named directory. Both require the `delete` capability on the path, are declared `destructive = true` and `idempotent = false`, and return `deleted: false` with an error when the path is already gone. `delete_file` reports `bytes` — the size the file had before deletion.

The call presentation marks recursion directly in the row title ("Delete directory (recursive)"), and the result — the deleted file size. `WorkspaceChangeTracker` watches both tools: a deleted file appears in the run changes as `Deleted` with the line count taken from the content captured before the run.

Check: 395 tests in four test projects — 120 Application, 178 Infrastructure, 67 Web, 30 Domain — with no failures, solution and test projects compilation without warnings. The full `dotnet run --project build -- verify` was not run: the running `AI.Host` instance was holding the assemblies in its `bin`, so the test assemblies were run directly.

## 2026-09-23 — explicit context checkpoint in expanded turns

The chat feed now marks a successful `mcp_app__context_compact` call with a small checkpoint row after
its tool group, only while the intermediate turn is expanded. The marker uses the persisted
structured result and appears when the result arrives; pending, failed, and malformed results
do not create it. Collapsing the turn hides it, and the final assistant answer remains a separate
message. This covers the explicit tool checkpoint, not the automatic planner fallback above.

Check: Web build completed with zero warnings or errors; all 102 Web tests passed with the xUnit
v3 runner, including the pending-result and completed-turn projection cases.

Follow-up: the expanded activity API originally omitted every tool result, including the
checkpoint result needed by the row. It now includes only small results for the app's
`context_compact` tool while other tool output remains lazy. The feed recognizes the persisted
`mcp_app__context_compact` name. Checks: Server build with zero warnings, 420 Server tests passed
with one platform skip, and all 102 Web tests passed.

## 2026-10-02 — zoom controls on rendered diagrams

A rendered ```mermaid block now carries its own zoom toolbar: zoom out, the current level, zoom in
and reset. The toolbar is a sibling of the diagram inside the same block, so it scrolls with the
diagram only in the direction that matters — the block stays put while the SVG pans under it — and
it is right-aligned above the diagram rather than floating over it, so no node is ever covered.

The controls are built by `js/mermaidBlocks.js`, not by Razor: the module replaces the fence element
itself, so no component markup ever sees the block. Clicks are handled by one delegated listener on
the feed container, because Blazor re-creates the surrounding markup as the transcript diffs; a
listener per button would be lost on the next re-render. Zoom is applied as width and height in px
on the SVG, not as a `transform: scale()` — a transform leaves the layout box untouched, so an
enlarged diagram would overlap the message below it and the block would not scroll to its edges.
The percentage is a multiple of the diagram's fitted size, measured each time with mermaid's own
inline style restored first: measuring the already-zoomed box would compound the steps, and a
stored measurement would go stale when the chat column changes width. Reset writes that original
style back, so 100% reproduces exactly what mermaid drew. Steps are 25%, the range is 50–400%, and
both end buttons disable themselves at their limit instead of silently ignoring a click. The error
state deliberately gets no toolbar: it is a diagnostic to read, and the block keeps its existing
scrolling for the preserved source.

Check: `node --check` on the module passes, all `--color-*` tokens used by the new CSS resolve to
real variables in the theme blocks. The solution build was not run to a green state: it currently
fails on pre-existing errors in unrelated, uncommitted work (`AppGuide*` / `Home.Guide.cs` and a
`CA1827` in `AppAskUserTool.cs`), none of which this increment touches. Both files changed here are
static assets, so nothing in the compiled project depends on them.

## 2026-10-03 — adaptive context budgets and stable request selection

Implemented [ADR-009](decisions/ADR-009-adaptive-context-policy.md). A single transient
`AdaptiveContextPolicy`, behind `IAdaptiveContextPolicy`, owns all window-dependent budgets,
instruction variants and admission, tool ranking and carried-set replacement. The independent
selector/priority/enrichment services were removed. Standing instructions are prepared once per
run using its actual connection, and step guidance stays trailing.

Small windows receive compact application guidance and bounded memory/skill indexes. User rules
and the current request remain intact; impossible requests report the full cost breakdown and
avoid a futile LLM summary. Discovery priorities combine searches in one batch and are consumed
by the next selection. Tool schemas no longer embed a changing omitted-name catalogue. Tool sets
preserve their order while fitting, and definitions covered by an automatic checkpoint are
reselected before final planning. Optional skill routing is skipped when its catalogue cannot fit.

The project preview shows the profile, resolved window and recommended instruction/tool shares
using existing settings rows and styles. It identifies the project/default connection scope and
shows oversized user rules as retained. The context, instruction, memory and skills documents
were updated together with the new ADR.

Checks: solution build with `--no-restore -m:1 /nodeReuse:false`, zero warnings/errors; 815 Server
tests in the final serial run (814 passed, one Unix-permissions skip); all 427 Web tests passed;
`git diff --check` passed. Tests include complete requests with generated App MCP schemas across
4K–128K windows, real chat orchestration on small windows, reserve changes, priorities, checkpoint
release, discovery batches and cached-prefix stability. No external LLM is used.

Verification note: one parallel Server run failed the existing subtask transcript test because
its generated MCP schema unexpectedly required `progress`. That test passed in isolation and
the complete final serial suite passed. The intermittent parallel schema issue was not changed
by this increment.

## 2026-10-03 — measured adaptive compaction and structured result retention

Implemented [ADR-010](decisions/ADR-010-adaptive-compaction-and-estimation.md). All adaptive
compaction decisions now come from `IAdaptiveContextPolicy`: trigger, retry growth, required
actual savings, target size, recent keep allowance, summary targets and estimation safety.
Agent orchestration and final planning share the message allowance after tools, guidance and
reserves. Both successful and unsuccessful automatic attempts wait for input growth before retrying.

Automatic checkpoints compare complete projections before and after, including summary framing.
An inflated or insufficiently useful summary leaves the previous checkpoint intact. Completed
current-turn work is shortened deterministically without a summarizer, while the current question
and latest parallel call/result exchange are retained. Synthetic summaries have an explicit
application-only origin marker so their user role cannot displace the current question. Kept LLM
fallback summaries contain exactly the version sent, including any final shortening.

The transient `IToolResultContextProjector` retains bounded structured outcomes, errors, paths,
identifiers and middle-of-output diagnostics before excerpts. Unknown and malformed results use
diagnostic lines and excerpts. The shared UTF-8 estimator bounds multilingual summaries and counts
preview source tokens. A bounded singleton stores numeric provider estimate/report observations;
the policy increases the baseline safety reserve for observed underestimation, isolated by
connection, endpoint and requested model. Estimated usage never calibrates the reserve.

Checks: solution build with `--no-restore -m:1 /nodeReuse:false`, zero warnings/errors; all 92
targeted context/checkpoint/usage tests passed; the final complete serial Server run had 841 tests
(840 passed, one Unix-permissions skip); all 427 Web tests passed; `git diff --check` passed.
The added tests exercise 64-step runs across 8K–128K windows with discovery, checkpoints, a large
result and a smaller-model transition, and 80-step automatic-summary sequences. They bound
compaction/prefix-change counts and verify mandatory text, complete parallel protocols, checkpoint
rejection, multilingual sizing, calibration isolation and provider-only observations. No external
LLM is used.
