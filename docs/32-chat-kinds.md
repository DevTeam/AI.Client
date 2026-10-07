# Extensible chat kinds

Status: Implemented for Conversation, Guide, Demo, Team demo and Scheduled. The other kinds below are design examples only.

## Current contract

`ChatKind` is an open, stable identifier. A chat stores its kind, a version number, and an optional JSON state value alongside the common message and branch graph. `IChatKindPolicy` is the sole registration for the behavior of a kind. `IChatKindPolicyRegistry` rejects duplicate registrations and refuses an unregistered kind; it does not silently reinterpret one as a conversation. Policies are DI-resolved services. The shared chat service, run dispatcher, agent, tools, hosted service, and UI consume policy decisions or generic presentation fields instead of a switch over known kinds.

The current policies are:

| Kind | Behavior |
| --- | --- |
| `conversation` | Normal visible, durable chat and run behavior. |
| `guide` | Guide interaction surface, restricted tools and navigation, full context, overlay questions, hidden from the normal chat list, and host-start cleanup. |
| `demo` | Normal visible execution with guide-owned sample messages and cleanup while untouched. A same-title conversation is not a demo. |
| `scheduled` | Normal visible chat and runs; its versioned state is a schedule, and the policy starts and stops the dispatcher that runs it. See [Scheduled chats](35-scheduled-chats.md). |

The chat and run repositories route by the policy's persistence choice. Durable storage uses the existing JSON repositories. Host-lifetime storage keeps chat documents and run state in memory and is available for a future policy to select; no current kind selects it. Both routes use the same chat and run APIs. An unattended submission is available to trusted host components; it is recorded with the queued message so a retry remains unattended. Such a run cannot wait for a user prompt or tool approval that requires a person.

The frontend receives generic visibility, navigation, and interaction-surface fields. The existing guide screen uses the `guide` interaction surface for its tour-specific UI. A kind that uses the ordinary chat surface needs no frontend branch. A new interaction surface with new controls is a separate UI feature, beyond registration of a chat kind.

Schema 9 writes kind and versioned state into full chat documents, and kind into summary documents. Reading older full documents maps `IsGuide` to Guide and `GuideMode=demo` to Demo. Older summary documents are rebuilt once from their full document because they did not record `GuideMode`. Unknown kinds remain readable as data, stay out of the ordinary chat list, and cannot be run until their policy is registered.

To add a kind that fits the existing chat and UI contracts, define its `ChatKind` value and `IChatKindPolicy`, then register that policy in the Pure.DI collection. The policy can select durable or host-lifetime storage, its model context and tools, user interaction, visibility, cleanup, and lifecycle callbacks. A kind may also provide its own creation source or typed state reader within its module. Common services, JSON serializers, repositories, and frontend kind lists do not change.

The tests register a fourth, test-only kind with versioned state and host-lifetime storage. They create it through `ChatService`, execute it through `ChatRunDispatcher` and `ChatAgent`, verify the reply, and verify that the chat disappears after a host restart. This exercises the extension path without a fourth-kind branch in the shared pipeline.

## Future project placement

Chat kind and project purpose are separate dimensions. A future Scheduled chat may belong to a **special Scheduled project**, which may be visible or hidden according to the project design. A future ephemeral chat may belong to a **special Temporary project** instead of a normal user project. Project identity must come from stable metadata, never from a display name. Project visibility and lifetime belong to the project layer; the chat policy chooses or requests placement through a project-placement boundary. The current project layer has no special-project implementation, so creating those projects remains future work.

## Changing the kind of a chat

`IChatService.ChangeKindAsync` reads a chat's kind and state and replaces them in one step under the chat's lease; `ChatThread.SetKind` keeps every message and branch. The new kind's policy validates the state, and a kind with another persistence route is refused, because the chat would be left behind in the old route. A conversation becomes `scheduled` and back this way.

## Scheduled

Scheduled is the first kind added through this contract after the migration: a policy, a versioned state shape (`ChatSchedule`), and a dispatcher the policy starts with the Host. It stays in the chat's own project and submits interactive runs, because a run that needs an approval or an answer waits for the person with the usual attention marker. No Scheduled branch was needed in `ChatService`, `ChatAgent`, `ChatRunDispatcher`, the chat repositories or the frontend chat list; the stopwatch icon reads `ChatSummary.Kind`. See [Scheduled chats](35-scheduled-chats.md).

Other possible growth points, without implemented identifiers or behavior: background/service chat, ephemeral chat, task/automation chat, imported chat, and subtask chat. They are examples, not requirements. In particular, making Guide ephemeral is a separate behavioral change and is not part of this migration.
