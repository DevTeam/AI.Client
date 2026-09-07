# MCP-интеграция

Статус: Accepted

> Текущее исполнение (2026-09-07): реализован встроенный stdio-сервер с `process_run`, потоковый агентский цикл Chat Completions, подтверждения и история вызовов. См. [инструменты по умолчанию](16-default-mcp-tools.md). Описания остальных серверов, транспортов и Responses API ниже относятся к целевой архитектуре; ранние preview-разделы отражают предыдущие этапы.


## Текущий статус configuration UI

Project settings хранят MCP server bindings и per-tool policies. Для встроенного `Default tools` реализованы stdio, discovery и execution; кнопка `Discover tools` в настройках проекта загружает инструменты и позволяет задать политику. Для сторонних серверов сохраняется только конфигурация: их процессы и HTTP transports пока не открываются.

## Версия и negotiation

Клиент использует официальный C# SDK Model Context Protocol и согласовывает protocol revision через MCP initialization. Нельзя жёстко предполагать draft-функции без negotiated capability.

Основные стандарты:

- [MCP specification](https://modelcontextprotocol.io/specification/2025-11-25)
- [MCP Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP Authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)

## Транспорты

### stdio

Используется для локальных History и FileSystem servers. Host запускает только предварительно зарегистрированные executable и arguments. Команда запуска никогда не формируется из model output или browser request.

### Streamable HTTP

Используется для удалённых MCP servers. Legacy SSE не используется для новых подключений. Для защищённых серверов реализуется стандартный OAuth flow.

## Lifecycle

1. Создать transport.
2. Выполнить MCP initialization и сохранить negotiated capabilities.
3. Запросить `tools/list` со всей пагинацией.
4. Нормализовать и закэшировать descriptors.
5. Подписаться на `notifications/tools/list_changed`, если объявлен `listChanged`.
6. При notification повторить list, пересчитать schema hashes и переоценить policies.
7. Корректно завершить session и transport.

## Идентичность инструмента

Имя уникально только внутри одного сервера. Внутренний ключ:

```text
ToolIdentity = ConfiguredMcpServerId + ToolName + ToolSchemaHash
```

`serverInfo.name` не используется как уникальный идентификатор. Для передачи в OpenAI применяется безопасный alias, например:

```text
mcp_{shortServerId}__{normalizedToolName}
```

Registry хранит обратное соответствие alias → original ToolIdentity.

## Tool descriptor

Сохраняются без потерь:

- `name`;
- `title`;
- `description`;
- `inputSchema`;
- `outputSchema`;
- `annotations`;
- `execution`;
- `_meta`, когда это требуется для round trip.

JSON Schema валидируется до показа инструмента модели. Некорректный инструмент отключается с диагностикой.

## Tool annotations

Поддерживаются стандартные hints:

- `readOnlyHint`;
- `destructiveHint`;
- `idempotentHint`;
- `openWorldHint`.

Annotations являются недоверенными hints, а не ACL. Они используются для UI и оценки риска только после определения trust level сервера. Политика проекта остаётся обязательной.

## Системные и агентские подключения

### Системные

History/Project MCP вызывается application layer и не передаётся модели. Его инструменты обслуживают projects, chats, branches, messages и audit.

### Агентские

Передаются модели после фильтрации:

```text
tools/list
→ schema validation
→ trust evaluation
→ project policy
→ alias mapping
→ provider tool definitions
```

## History MCP

Минимальные инструменты:

```text
project_list, project_get, project_create, project_update, project_delete
chat_list, chat_get, chat_create, chat_delete, chat_copy
branch_list, branch_create, branch_rename, branch_move_head
message_append, message_get_path, message_search
audit_list
```

Для чтения дополнительно публикуются resources:

```text
project://{projectId}
project://{projectId}/chats/{chatId}
project://{projectId}/chats/{chatId}/messages/{messageId}
project://{projectId}/audit
```

## FileSystem MCP

Каждый tool имеет одну ясную операцию и корректные annotations. Многоцелевой `filesystem` с параметром `operation` не используется: отдельные tools легче авторизовать и объяснить пользователю.

`edit` принимает expected content hash и структурированный patch/replacements. `write` не заменяет существующий файл без явного параметра и соответствующей политики. `delete` считается destructive независимо от annotations.

## OAuth для HTTP MCP

Поддерживаются OAuth 2.1, RFC 9728 Protected Resource Metadata, RFC 8414/OIDC discovery, RFC 8707 Resource Indicators и PKCE S256. Scopes запрашиваются постепенно, например:

```text
files:read
files:write
files:delete
history:read
history:write
```

Host обрабатывает `401` и `403 insufficient_scope` через `WWW-Authenticate`. Широкие wildcard scopes по умолчанию не запрашиваются.
# Global MCP configuration

MCP servers являются глобальными и доступны всем проектам. Первая итерация реализует только JSON storage и UI конфигурации:

- transports `StreamableHttp` и `Stdio`;
- enable/disable;
- server-wide policy `Allow`, `Ask` или `Deny`;
- command, arguments, working directory и environment variables для stdio;
- write-only credential для Streamable HTTP.

Подключение и вызовы сторонних серверов остаются отложенными. Встроенный `Default tools` доступен автоматически; его запуском управляет Host, а обнаружение инструментов и per-tool policies доступны в настройках проекта.
