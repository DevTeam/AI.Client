# ADR-004: Responses API как основной OpenAI-протокол

Статус: Accepted

Дата: 2026-08-11

## Контекст

Клиент должен поддерживать agentic tool calling, streaming, multi-turn history и несколько OpenAI-compatible endpoints. Совместимость провайдеров неоднородна.

## Решение

Для OpenAI основным адаптером является Responses API. Chat Completions остаётся fallback для endpoints без Responses. Application layer использует provider-neutral `IAIEndpoint` и `AgentEvent`; типы OpenAI SDK остаются в Infrastructure.

## Последствия

Положительные:

- современная item-модель для messages и tool calls;
- естественная поддержка агентского цикла;
- streaming и continuation;
- provider details изолированы адаптером.

Отрицательные:

- нужны два адаптера;
- feature parity совместимых providers нельзя предполагать;
- local history должна уметь восстановить полный контекст без provider state.

## Источники

- [Migrate to the Responses API](https://developers.openai.com/api/docs/guides/migrate-to-responses)
- [Function calling](https://developers.openai.com/api/docs/guides/function-calling)
- [Conversation state](https://developers.openai.com/api/docs/guides/conversation-state)

