# Extensible chat kinds

Status: Implemented for Conversation, Guide, and Demo. The other kinds below are design examples only.

## Current contract

`ChatKind` is an open, stable identifier. A chat stores its kind, a version number, and an optional JSON state value alongside the common message and branch graph. `IChatKindPolicy` is the sole registration for the behavior of a kind. `IChatKindPolicyRegistry` rejects duplicate registrations and refuses an unregistered kind; it does not silently reinterpret one as a conversation. Policies are DI-resolved services. The shared chat service, run dispatcher, agent, tools, hosted service, and UI consume policy decisions or generic presentation fields instead of a switch over known kinds.

The current policies are:

| Kind | Behavior |
| --- | --- |
| `conversation` | Normal visible, durable chat and run behavior. |
| `guide` | Guide interaction surface, restricted tools and navigation, full context, overlay questions, hidden from the normal chat list, and host-start cleanup. |
| `demo` | Normal visible execution with guide-owned sample messages and cleanup while untouched. A same-title conversation is not a demo. |

The chat and run repositories route by the policy's persistence choice. Durable storage uses the existing JSON repositories. Host-lifetime storage keeps chat documents and run state in memory and is available for a future policy to select; no current kind selects it. Both routes use the same chat and run APIs. An unattended submission is available to trusted host components; it is recorded with the queued message so a retry remains unattended. Such a run cannot wait for a user prompt or tool approval that requires a person.

The frontend receives generic visibility, navigation, and interaction-surface fields. The existing guide screen uses the `guide` interaction surface for its tour-specific UI. A kind that uses the ordinary chat surface needs no frontend branch. A new interaction surface with new controls is a separate UI feature, beyond registration of a chat kind.

Schema 9 writes kind and versioned state into full chat documents, and kind into summary documents. Reading older full documents maps `IsGuide` to Guide and `GuideMode=demo` to Demo. Older summary documents are rebuilt once from their full document because they did not record `GuideMode`. Unknown kinds remain readable as data, stay out of the ordinary chat list, and cannot be run until their policy is registered.

To add a kind that fits the existing chat and UI contracts, define its `ChatKind` value and `IChatKindPolicy`, then register that policy in the Pure.DI collection. The policy can select durable or host-lifetime storage, its model context and tools, user interaction, visibility, cleanup, and lifecycle callbacks. A kind may also provide its own creation source or typed state reader within its module. Common services, JSON serializers, repositories, and frontend kind lists do not change.

The tests register a fourth, test-only kind with versioned state and host-lifetime storage. They create it through `ChatService`, execute it through `ChatRunDispatcher` and `ChatAgent`, verify the reply, and verify that the chat disappears after a host restart. This exercises the extension path without a fourth-kind branch in the shared pipeline.

## Future project placement

Chat kind and project purpose are separate dimensions. A future Scheduled chat may belong to a **special Scheduled project**, which may be visible or hidden according to the project design. A future ephemeral chat may belong to a **special Temporary project** instead of a normal user project. Project identity must come from stable metadata, never from a display name. Project visibility and lifetime belong to the project layer; the chat policy chooses or requests placement through a project-placement boundary. The current project layer has no special-project implementation, so creating those projects remains future work.

## Scheduled as an extension example

Scheduled is not a registered kind. A future implementation would add a policy and a versioned state shape for schedule data, plus a scheduler that participates in host startup and shutdown. The scheduler would create or find its special project and submit through the unattended dispatch path. The chat policy would decide model and tool behavior, visibility, persistence, and cleanup. Project visibility would be set by the project layer, independently of chat visibility. None of these steps requires a Scheduled branch in `ChatService`, `ChatAgent`, `ChatRunDispatcher`, the chat repositories, or the current frontend chat list.

Other possible growth points, without implemented identifiers or behavior: background/service chat, ephemeral chat, task/automation chat, imported chat, and subtask chat. They are examples, not requirements. In particular, making Guide ephemeral is a separate behavioral change and is not part of this migration.
