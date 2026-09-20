# Compact turn view in chat

Status: Accepted (implemented)

UI sketch: [sketches/compact-turn-view.html](sketches/compact-turn-view.html) — open it in a browser; it contains all states of the row in the application's colors.

## Task

A single model turn (from a user message to the final response) currently unfolds into several items in the feed: each pair of "intermediate reasoning + tool calls" is a separate block. A five-cycle turn takes up the whole screen, and the final answer scrolls down off the bottom.

Requirement: by default, show only the dialog — the user message, the final response, and the file-change statistics. Intermediate messages are collapsed into **one row**, and clicking it expands them into today's view without changes.

## What already exists

- [`ChatFeed.BuildFeedItems`](../src/AI.Client.Web/Components/ChatFeed.cs) projects the flat chain of branch messages onto feed items: either a single message or a "preamble + tool calls" group.
- [`MessageFeed.razor`](../src/AI.Client.Web/Components/MessageFeed.razor) renders these items; `GetRenderedFeedItems()` takes the tail of the list by `_renderedItemLimit`.
- [`ToolActivityGroup`](../src/AI.Client.Web/Components/ToolActivityGroup.razor) already collapses a batch of calls into a one-line summary and expands the rows on click. **This component is not changed.**
- [`WorkspaceChanges`](../src/AI.Client.Web/Components/WorkspaceChanges.razor) is the "N files changed" card. **Not changed.**
- `composer-status` in [`Home.razor`](../src/AI.Client.Web/Pages/Home.razor) shows `Generating… 12s` and hints about Enter. **Not changed, nothing new is added above the input area.**

## Decisions

### Turn boundary

A turn is a segment of the feed from a user message (not including it) to the next user message or the end of the branch.

Within a turn, the **final answer** is the last element of the segment if it is a single assistant message without tool calls. Everything else inside the segment is **intermediate messages**: both tool groups and plain assistant messages without tools.

A turn can end without a final answer (stopped, failed) — then all elements of the segment are intermediate.

### Collapsed row

One row per turn, in place of all intermediate messages — that is, between the user message and the final answer.

| Turn state | Row text |
| --- | --- |
| Running | current model action + time: `Gathering class usage data… · 12s` |
| Completed | `Worked for 1m 13s` |
| Stopped by user | `Stopped after 22s` |
| Failed | `Failed after 22s` |
| No intermediate messages | no row at all |

Only time — no step count, no call count, no error badge. Errors are visible after expanding: today's `ToolActivityGroup` is there with its own `N failed`.

"Current action" is the text of the latest intermediate assistant message — it changes with each cycle. An intermediate message whose content is identical to the previous one does not update the row, so a reasoning chain that repeats itself does not flicker. When the row is built from tool groups only, the latest group's first tool name appears in the row; the row already names a tool, and adding the model text is not necessary.

If the latest message is an assistant response without tools but with text, the row shows that text. Pure tool groups with no reasoning also update the row to their tool name.

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

◌ Gathering class usage data… · 12s  ›                     ← text changes with each step
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
4. A row of a running turn shows the latest action of the model; on repeated identical text, the row does not flicker; a half-rendered state does not require a skeleton.
5. During generation the row shows the current model action and the growing time; the feed itself does not grow in the number of items.
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
