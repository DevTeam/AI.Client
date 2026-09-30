# Compact turn view in chat

Status: Accepted (implemented)

UI sketch: [sketches/compact-turn-view.html](sketches/compact-turn-view.html) — open it in a browser; it contains all states of the row in the application's colors.

## Task

A single model turn (from a user message to the final response) currently unfolds into several items in the feed: each pair of "intermediate reasoning + tool calls" is a separate block. A five-cycle turn takes up the whole screen, and the final answer scrolls down off the bottom.

Requirement: by default, show only the dialog — the user message, the final response, and the file-change statistics. Intermediate messages are collapsed into **one row**, and clicking it expands them into today's view without changes.

## What already exists

- [`ChatFeed.BuildFeedItems`](../src/AI.Web/Components/ChatFeed.cs) projects the flat chain of branch messages onto feed items: either a single message or a "preamble + tool calls" group.
- [`MessageFeed.razor`](../src/AI.Web/Components/MessageFeed.razor) renders these items; `GetRenderedFeedItems()` takes the tail of the list by `_renderedItemLimit`.
- [`ToolActivityGroup`](../src/AI.Web/Components/ToolActivityGroup.razor) already collapses a batch of calls into a one-line summary and expands the rows on click. **This component is not changed.**
- [`WorkspaceChanges`](../src/AI.Web/Components/WorkspaceChanges.razor) is the "N files changed" card. **Not changed.**
- `composer-status` in [`Home.razor`](../src/AI.Web/Pages/Home.razor) shows `Generating… 12s` and hints about Enter. **Not changed, nothing new is added above the input area.**

## Decisions

### Turn boundary

A turn is a segment of the feed from a user message (not including it) to the next user message or the end of the branch.

Within a turn, the **final answer** is the last element of the segment if it is a single assistant message without tool calls. Everything else inside the segment is **intermediate messages**: both tool groups and plain assistant messages without tools.

A turn can end without a final answer (stopped, failed) — then all elements of the segment are intermediate.

### Collapsed row

One row per turn, in place of all intermediate messages — that is, between the user message and the final answer.

| Turn state | Row text |
| --- | --- |
| Running | `Working for 12s`; the model's text is shown in full below the row (see [Live text](#live-text)), and what the turn is doing under it (see [Activity line](#activity-line)) |
| Completed | `Worked for 1m 13s` |
| Stopped by user | `Stopped after 22s` |
| Failed | `Failed after 22s` |
| No intermediate messages | no row at all |

Only time — no step count, no call count, no error badge. Errors are visible after expanding: today's `ToolActivityGroup` is there with its own `N failed`.

The row no longer carries the model's text: a single truncated line of it was unreadable. The text moved under the row, in full — see [Live text](#live-text).

### Live text

While the turn runs, the model's current text is shown under the row as an ordinary assistant message, streamed as it is written. It may turn out to be the final answer, so it is written like one: when the turn ends that way, the answer is already in place and nothing jumps.

Source, newest first: the draft of the model step in flight (`ChatRunSnapshot.DraftContent`, published by the server as the model streams, never persisted), else the text of the turn's latest intermediate assistant message.

- **The text stays until the next one starts.** When the model moves on to tools, its text stays up; the row shows the tool. A step that lands as a preamble with the same text is a continuation, not a new text.
- **The next text waits until the previous could be read.** Reading time is length ÷ 20 characters per second, clamped to 1.5–6 s, counted from when the shown text last grew. A note that finished before a slow tool call has usually been read by then, so most switches cost no wait.
- **No queue.** When the wait is over, the newest text is shown; anything in between is in the expanded turn.
- **Hold.** While the pointer is over the text, it is not replaced.
- **No going back.** A text the turn has moved past is not shown again (a draft rejected by the completion protocol leaves an older note as the newest candidate).
- **The end is immediate.** Final answer, stop or failure replace the live text at once; the reader is not made to wait for the thing the wait was for.
- **Expanded turn:** the notes are shown as themselves, so only the draft of the step in flight is added at the end.
- A replacement fades in from the row (300 ms); growth of the same text is patched in place. `prefers-reduced-motion` turns the animation off.

The logic lives in `TurnLiveText` (one instance per transcript) and is covered by `TurnLiveTextTests`.

#### Preamble

A text known to lead into tool calls is a remark, not the answer, and is drawn muted and slightly smaller (`.live-text.is-preamble`). It is known once the model step in flight has started a call (`ChatRunSnapshot.DraftToolCall`, see below) or once the text has landed as a note with tool calls attached. Until then it is drawn as a possible answer: most answers are not preceded by anything that would say otherwise. The completion protocol's own tool (`app_finish_run`) does not mark the text — it carries the answer.

### Activity line

One muted line under the live text says what the turn is doing now. A model often ends its note with "…and run it:" and then streams the call's arguments for seconds; without the line the colon was followed by a blank.

| Run state | Line |
| --- | --- |
| A tool is running | `› <tool title>  <detail>` — the last active call, as its tool row names it |
| The model is streaming a call's arguments | `› Preparing <tool title>…` |
| Waiting for tool approval | `› Waiting for tool approval` |
| The model is writing prose | no line: the text itself is the activity |
| Otherwise (request in flight, after a result) | `› Thinking…` |
| A question to the user, a rate-limit wait | no line: they have their own cards |

The line is static — no spinner or animation — so it reads as work in progress with animations off. It follows the run at once; it does not wait for the live text's reading delay. It is shown only while the turn is collapsed: expanded, the live tool rows say the same. The row itself no longer names the running tool, which would say the same thing twice.

The start of a call comes from the stream: `ChatCompletionSseParser` raises `ToolCallsStarted` with `ToolCallName` as soon as the endpoint names the call, the agent reports it, and the dispatcher publishes it at once as `DraftToolCall`. It is cleared together with the draft, in the publication that lands the preamble.

The row is not interactive during generation — click and expand are available only after the turn ends, so a half-rendered intermediate state does not need a skeleton.

### Transition

The row replaces the existing intermediate elements in the feed only after the turn ends. During generation it is appended below the user message as a separate element, while the existing feed items still grow as today.

If the user sent the next message during generation, the new user message is placed below the running turn's row. When the turn ends, the intermediate elements are removed, the row assumes its final text, and the next user message stays at the bottom of the feed.

### Why during generation is a separate element

The intermediate elements already rendered today are needed for their content — reasoning, call rows, results — and for that there is only today's representation. While the turn is running, the items are appended to the existing feed, and the row is a separate element below them so that the latest current-action text is visible without scrolling. After the turn ends, the intermediate elements are removed and the row takes their place.

### Live tail

The tail at the bottom of the feed continues to grow: the running turn's row stays visible even when the user scrolls up, because the feed is rendered from the tail. A row of a completed turn scrolled above the tail stays in its place.

### Step counter

Step count is not shown. Reasoning models naturally make more intermediate cycles, and a long row `Thought for 23s · 4 steps` is not informative — the row is about waiting, not about how many cycles it took.

### Failure in the row

If the turn ends with an error, the row keeps `Failed after …` and shows no badge. After expanding, today's `ToolActivityGroup` shows `N failed` as today. The user gets to the failed calls from the card itself, not from the row.

### Click target

The row is the click target. Hover highlights the row and shows a chevron; keyboard focus adds a frame. Click or Enter/Space toggles the section.

### Animation

The collapse is animated as an instant change without slide or fade, so a conversation does not appear to "twitch". Expanding today is a layout change that is rendered synchronously.

## Implementation

In `ChatFeed.BuildFeedItems`:

- intermediate items are grouped into a `CollapsedTurn(turnId, items, final)` instead of being placed in the feed;
- the final answer and the user message that started the turn stay separate items;
- if there are no intermediates, no `CollapsedTurn` is created — the user message and the final answer are rendered without it.

In `MessageFeed.razor`:

- `RenderCollapsedTurn` renders a row in collapsed form;
- a separate `RenderIntermediates` section appears under the row when expanded;
- expanded state is held by `turnId` in the component state; switching chats or scrolling does not lose it.

`ToolActivityGroup` and `WorkspaceChanges` are unchanged.

The data goes into the row directly from the snapshot, not from the message text — the visible intermediate text can change, but the row text is taken from the latest message in the snapshot.

## Examples

Collapsed, completed:

```
Look where classes are used without DI

  ✓ Tools finished        1 tool call            164 ms

  Gathering class usage data in Web/Cli/Host.
  ✓ Tools finished        8 tool calls · 1 failed  2.2 s

  → 7 classes are created without interfaces and outside DI…

  3 files changed                              +128  −14   ⌄
```

Note the missing row: in this example, the final answer is a plain text without tool calls. The intermediate items are visible; they belong to previous runs of the same branch.

A row without a final answer (stopped, failed):

```
Look where classes are used without DI

  Analyzing the structure and DI registration points.        ← turn row
  ✓ Tools finished        1 tool call            164 ms

  Gathering class usage data in Web/Cli/Host.
  ✓ Tools finished        8 tool calls · 1 failed  2.2 s

  7 classes are created without interfaces and outside DI…   ← final answer
```

Running turn:

```
  Look where classes are used without DI

◌ Read file · 12s  ›                                       ← running tool, or "Working for 12s"

  Gathering class usage data in Web/Cli/Host. First I'll   ← live text, in full, streaming;
  look at where the registrations are▌                        the next one waits until this is read
```

Expanded:

```
Worked for 1m 13s  ⌄

  I will study the structure and DI registration points.   ← today's markup,
  ✓ Tools finished        1 tool call            164 ms       nothing changed

  Gathering class usage data in Web/Cli/Host.
  ✓ Tools finished        8 tool calls · 1 failed  2.2 s

  7 classes are created without interfaces and outside DI…
```

## Styling

The row is rendered in a muted color, smaller text, single line with ellipsis on overflow. Hover and focus states highlight the background and the chevron. On running, the chevron is animated. On failed, the text is the same muted color; the chevron color does not carry meaning — it is the expanded group that shows `N failed`.

## Acceptance criteria

1. The feed shows only the user message, the final answer, the workspace changes card, and a single row between the user message and the final answer.
2. The row's text matches the table by turn state.
3. Click on the row expands the intermediate items into today's view without changes.
4. A running turn shows the model's current text in full under the row, streamed; a new text replaces it only after the reading delay (see [Live text](#live-text)).
5. During generation the row shows `Working for …` and the growing time; the feed holds at most one live text for the turn, and one activity line under it.
6. A turn with no intermediate messages does not add a row.
7. Stopped and failed turns show `Stopped after …` / `Failed after …`.
8. The file changes card is visible with the turn collapsed.
9. Nothing has changed above the input area.

## How others do it

For context, in case the decision is revisited:

- ChatGPT, Claude.ai, Cursor, Copilot agent, Windsurf — intermediate steps remain in the feed collapsed; after the turn, a row like "Thought for 23s" expanding to a summary.
- Devin moves progress out of the chat into a separate timeline panel.
- Claude Code (CLI) shows the current action in a row above the prompt; the feed itself does not grow.

The variant chosen here is closest to ChatGPT: one leftover row per turn, expanding on click.
