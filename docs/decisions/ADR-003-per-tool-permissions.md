# ADR-003: Права отдельно для каждого MCP-инструмента

Статус: Accepted

Дата: 2026-08-11

## Контекст

MCP ToolAnnotations сообщают risk hints, но спецификация требует считать их недоверенными для неизвестных servers. Универсальные категории `read/write/delete` не описывают произвольные инструменты достаточно точно.

## Решение

Каждый project хранит `Allow`, `Ask` или `Deny` отдельно для ToolIdentity, включающей configured server ID, tool name и schema hash. Новый или изменившийся инструмент получает `Ask`. Directory grants, OAuth scopes и server-side ACL дополнительно ограничивают вызов.

## Последствия

Положительные:

- явный контроль пользователя;
- schema change инвалидирует старое разрешение;
- одинаковые tool names разных servers не смешиваются;
- модель не может расширить права через annotations.

Отрицательные:

- требуется больше initial approvals;
- нужен удобный policy UI;
- большое число инструментов требует фильтрации и bulk operations, которые не должны ослаблять default `Ask` незаметно.

## Примечание

Annotations используются для объяснения риска и безопасного планирования, но не являются авторизацией. HTTP MCP authorization реализуется стандартным OAuth 2.1 flow; локальный `stdio` использует process/environment security.

