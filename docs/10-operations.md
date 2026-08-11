# Эксплуатация и диагностика

Статус: Accepted

## Локальный запуск

Пользователь запускает `AI.Client.Host`. Host:

1. Получает exclusive lock на data directory.
2. Проверяет schema versions и незавершённые atomic writes.
3. Поднимает loopback endpoint.
4. Раздаёт WASM UI.
5. Открывает браузер после готовности endpoint.

PWA может кэшировать UI shell, но agent operations требуют работающий Host.

## Конфигурация

Разделяется на:

- non-secret Host configuration;
- protected credential store;
- project JSON documents;
- registered MCP executable registry;
- восстанавливаемый UI cache.

Пути data/log/cache отображаются в diagnostics UI.

## Логи

Структурированные logs содержат timestamp, level, component, project/chat/run IDs и correlation ID. Обязательна redaction:

- Authorization/API-key headers;
- OAuth tokens;
- credential fields;
- URL query secrets;
- tool arguments, помеченные sensitive локальной конфигурацией.

Полный file content по умолчанию не логируется.

## Диагностический пакет

Экспортирует:

- версии Host/WASM/.NET;
- negotiated MCP protocol revisions и capabilities;
- endpoint capability snapshots без credentials;
- schema versions;
- redacted recent logs;
- failed run metadata;
- storage integrity report.

Перед сохранением пользователь видит состав пакета.

## Backup

Backup создаётся из согласованного snapshot:

- остановить новые writes;
- дождаться текущих atomic commits;
- скопировать project JSON tree;
- проверить JSON schemas и content hashes;
- записать manifest с SHA-256.

Credentials экспортируются только отдельной явно защищённой процедурой и не входят в обычный project export.

## Recovery

- `.tmp` без final file удаляется после проверки.
- Immutable orphan nodes не подключаются автоматически.
- Повреждённый ref восстанавливается только из backup/audit с подтверждением.
- Неизвестная schema version открывается read-only.
- Повреждённый content object маркирует зависимые сообщения, но не удаляется автоматически.

## Обновление

Перед миграцией Host создаёт backup. Миграции выполняются последовательно и проверяются до переключения active data directory. Downgrade не обещает запись в более новую schema, но должен сохранять данные и сообщать о read-only режиме.

## MCP process management

- process lifetime привязан к Host session или configured keep-alive;
- stdout зарезервирован для MCP stdio protocol;
- diagnostics server пишет в stderr;
- crash ограничивается backoff/restart budget;
- бесконечный restart loop запрещён;
- executable/schema changes требуют повторной оценки trust и policies.

## Метрики без telemetry

Локально доступны latency, token usage, tool count, retry count, context size и storage size. Внешняя telemetry по умолчанию отключена и не входит в MVP.

