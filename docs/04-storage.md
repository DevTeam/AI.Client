# Локальное JSON-хранилище

Статус: Accepted

## Выбранный формат

Хранилище использует дерево каталогов и неизменяемый JSON-файл на каждый message node. Метаданные проекта, refs веток, agent runs и аудит хранятся отдельно.

```text
data/
├─ index.json
└─ projects/
   └─ {projectId}/
      ├─ project.json
      ├─ security.json
      ├─ endpoints.json
      ├─ mcp-servers.json
      ├─ chats/
      │  ├─ index.json
      │  └─ {chatId}/
      │     ├─ chat.json
      │     ├─ nodes/
      │     │  └─ {messageId}.json
      │     ├─ content/
      │     │  └─ sha256-{hash}.json
      │     └─ runs/
      │        └─ {agentRunId}.json
      └─ audit/
         └─ yyyy-MM.json
```

## Общий envelope

Каждый документ содержит:

```json
{
  "$schema": "urn:ai-client:schema:project:1",
  "schemaVersion": 1,
  "documentType": "project",
  "createdAt": "2026-08-11T12:00:00Z"
}
```

До появления публичного schema URL используется стабильный URN. Все схемы основаны на JSON Schema Draft 2020-12.

## Формат project.json

```json
{
  "schemaVersion": 1,
  "documentType": "project",
  "id": "019f0000-0000-7000-8000-000000000001",
  "name": "AI.Client",
  "description": "Разработка AI-клиента",
  "createdAt": "2026-08-11T12:00:00Z",
  "updatedAt": "2026-08-11T12:00:00Z",
  "defaultEndpointId": "019f0000-0000-7000-8000-000000000002",
  "defaultModelId": "configured-model-id",
  "systemInstructions": "Работай только внутри разрешённых директорий."
}
```

## Формат chat.json

```json
{
  "schemaVersion": 1,
  "documentType": "chat",
  "id": "019f0000-0000-7000-8000-000000000010",
  "projectId": "019f0000-0000-7000-8000-000000000001",
  "title": "Архитектура",
  "revision": 12,
  "branches": {
    "019f0000-0000-7000-8000-000000000011": {
      "name": "main",
      "headMessageId": "019f0000-0000-7000-8000-000000000020"
    }
  }
}
```

## Формат node

```json
{
  "schemaVersion": 1,
  "documentType": "message-node",
  "id": "019f0000-0000-7000-8000-000000000020",
  "chatId": "019f0000-0000-7000-8000-000000000010",
  "parentId": null,
  "role": "user",
  "createdAt": "2026-08-11T12:05:00Z",
  "content": [
    {
      "type": "text",
      "text": "Опиши архитектуру"
    }
  ]
}
```

## Большие данные

Большие tool results и вложения сохраняются content-addressed:

```json
{
  "type": "content-reference",
  "sha256": "...",
  "mediaType": "application/json",
  "size": 145233
}
```

Content object после записи неизменяем. Проверка SHA-256 выполняется при чтении.

## Атомарность

Добавление сообщения:

1. Сериализовать node во временный файл в том же каталоге.
2. Flush и атомарно переименовать в окончательное имя.
3. Прочитать текущий `chat.json` и проверить `revision`.
4. Создать новый `chat.json` с новым head и `revision + 1`.
5. Атомарно заменить metadata file.

Если шаг 5 не выполнен, остаётся недостижимый node. Он не повреждает чат и позднее удаляется garbage collector.

## Конкуренция

Запись refs использует optimistic concurrency по `revision`. При конфликте операция перечитывает metadata и возвращает domain conflict; молчаливое last-write-wins запрещено.

## Миграции

- Чтение поддерживает текущую и предыдущую schema version.
- Миграция выполняется в новую временную директорию.
- Исходные данные не изменяются до полной проверки.
- После успеха директории заменяются атомарно либо сохраняется recoverable backup.
- Неизвестная более новая версия открывается read-only.

## Индексы и поиск

Индексы являются восстанавливаемым кэшем. Источником истины остаются project/chat/node documents. Поиск первой версии может сканировать JSON; отдельный индекс добавляется только после измерения производительности.

## Credentials

API keys, OAuth refresh tokens и session secrets не сохраняются в этом дереве. JSON содержит только `CredentialReference`.
