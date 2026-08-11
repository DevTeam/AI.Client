# ADR-001: Hosted Blazor WebAssembly

Статус: Accepted

Дата: 2026-08-11

## Контекст

Client-Side WebAssembly нужен как UI-платформа, но browser sandbox не может безопасно хранить AI credentials, запускать `stdio` MCP processes или получать произвольный доступ к локальной файловой системе.

## Решение

Использовать локальный ASP.NET Core `AI.Client.Host`, который раздаёт `AI.Client.Web` и предоставляет same-origin API. UI продолжает выполняться в browser WASM. Host хранит credentials, вызывает AI endpoints и управляет MCP transports/processes.

## Последствия

Положительные:

- secrets отсутствуют в WASM;
- доступен `stdio`;
- нет внутреннего CORS;
- один self-contained executable запускает систему;
- privileged operations централизованно контролируются.

Отрицательные:

- PWA не выполняет agent operations без Host;
- Host становится security boundary;
- требуются CSP, origin protection и жёсткий registry MCP executables.

## Отклонённая альтернатива

Standalone static WASM с BYOK отклонён как основной режим из-за утечки ключей, CORS и отсутствия `stdio`/filesystem access.

