# ADR-004: Responses API as the primary OpenAI protocol

Status: Accepted

Date: 2026-08-11

## Context

The client must support agentic tool calling, streaming, multi-turn history, and multiple OpenAI-compatible endpoints. Provider compatibility is uneven.

## Decision

For OpenAI, the Responses API is the primary adapter. Chat Completions remains a fallback for endpoints without Responses. The application layer uses the provider-neutral `IAIEndpoint` and `AgentEvent`; the OpenAI SDK types remain in Infrastructure.

## Consequences

Positive:

- a modern item model for messages and tool calls;
- natural support for the agent loop;
- streaming and continuation;
- provider details are isolated by the adapter.

Negative:

- two adapters are required;
- feature parity across compatible providers cannot be assumed;
- local history must be able to restore the full context without provider state.

## Sources

- [Migrate to the Responses API](https://developers.openai.com/api/docs/guides/migrate-to-responses)
- [Function calling](https://developers.openai.com/api/docs/guides/function-calling)
- [Conversation state](https://developers.openai.com/api/docs/guides/conversation-state)

