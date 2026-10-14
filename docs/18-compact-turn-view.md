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

While the turn runs, the model's current text is shown under the row, streamed as it is written, in the muted voice of every note (see [Notes window](#notes-window)). The final answer is not one of them: it streams in as its own message while the notes window folds away.

Source, newest first: the draft of the model step in flight (`ChatRunSnapshot.DraftContent`, published by the server as the model streams, never persisted), else the text of the turn's latest intermediate assistant message.

- **The text stays until the next one starts.** When the model moves on to tools, its text stays up; the row shows the tool. A step that lands as a preamble with the same text is a continuation, not a new text.
- **The next text waits until the previous could be read.** Reading time is length ÷ 20 characters per second, clamped to 1.5–6 s, counted from when the shown text last grew. A note that finished before a slow tool call has usually been read by then, so most switches cost no wait.
- **No queue.** When the wait is over, the newest text is shown; anything in between is in the expanded turn.
- **Hold.** While the pointer is over the text, it is not replaced.
- **No going back.** A text the turn has moved past is not shown again.
- **The end is immediate.** Final answer, stop or failure replace the live text at once; the reader is not made to wait for the thing the wait was for.
- **Expanded turn:** the notes are shown as themselves, so only the draft of the step in flight is added at the end.
- A new text surfaces in place out of a soft blur (300 ms); growth of the same text is patched in place. `prefers-reduced-motion` turns the animations and the gliding off.

#### Notes window

The texts the turn has moved past stay under the row, newest at the bottom: **Settings → Chat → Notes while working** sets how many, 1 to 5, 3 by default (`ClientSettings.LiveNoteCount`, one value for every chat on this client). With 1 the window is a two-line teleprompter for the current text.

The notes used to be a stack whose height changed with every note: the oldest folded away, the one before the newest settled to two lines and the new one dropped in from above, three height changes in different directions at once, so the stack and the activity line under it jerked on every step. They are now a window (`.live-notes`, sized by `js/liveNotes.js`):

- **Height only grows.** The window is as tall as the newest notes, at most two lines per kept note plus the gaps between them but never less than the newest note whole, and never lower than it has been during the turn; it starts empty and grows with the first note. Lowering the setting is the one thing that shrinks it. So the activity line under it only ever moves down, smoothly, and stops for good once the window reaches its cap.
- **One movement, always up.** Notes keep their own height; nothing folds or settles. The track is pinned to the bottom of the window: a line or a note arriving below is uncovered from beneath while the text above glides up (one `transform` and the window's height on the same curve, twice `--duration-slow` with `--ease-emphasized`), and a note dropped above moves nothing. A new note fades in in place rather than sliding in.
- **N notes and what fits above.** The window is sized for the newest N notes, so they are shown whole when they are of usual length. The newest note is always shown whole: one longer than the cap makes the window as tall as itself and pushes the older ones up under the window's top edge, which dissolves into the row (a mask over `--live-notes-fade`, set by the script and stopping short of the newest note) instead of cutting the text. Older notes the live text still remembers (`.live-text.is-older`; `TurnLiveText` keeps enough of them to fill the tallest window with one-line notes) stay mounted above, so room the window keeps from a longer earlier note is filled rather than left blank, and a note is only ever unmounted out of sight.
- **Rail.** A hairline on the left (`--color-border-subtle`) strings the notes together as steps, with a dot at the first line of each; the newest note's dot takes `--color-accent`.
- **One voice.** Every note, the newest included, is drawn in `--color-text-muted` at a slightly smaller size. They used to step back by age (`--color-text-subtle`, then `--color-text-faint`) and by whether they had led into a call, which gave the stack three or four greys and changed a note's colour and size under the reader as it aged. The newest is the one at the bottom, next to the activity line, and the one with the accent dot.
- **Writing.** While the model is still writing the newest note (the step in flight streams prose and has not started a call), a soft accent sheen runs through its words (`.live-text.is-writing`, a `background-clip: text` gradient on a 2.4 s loop); it stops the moment the step finishes or turns to a call. The same sheen (`.text-sheen`) runs through the row's "Working for …" while the model is busy and through the title of the activity line, and neither plays while the turn waits on the person (an approval, a question, a rate-limit wait). Links, code and other elements with their own background keep plain text. Off under reduced motion.
- **Hold.** While the pointer is over the window, no new note replaces the newest (see Hold above).
- **The answer.** When the final answer starts streaming, the window folds away upwards (its height and opacity go to zero, the text pinned to its bottom rising out of it) and the activity line goes.
- **Opened mid-run.** A turn presented for the first time (the chat or branch opened while it runs) fills the window from its earlier notes at once, without waiting for reading time. The transcript keeps the text of a branch's notes while its last turn has no answer, for this; the notes of answered turns are folded as before.

The logic lives in `TurnLiveText` (one instance per transcript) and is covered by `TurnLiveTextTests`.

### Activity line

One muted line under the live text says what the turn is doing between its notes. A model often ends its note with "…and run it:" and then streams the call's arguments for seconds; the line names the call being prepared or run.

| Run state | Line |
| --- | --- |
| A tool is running | `<tool title> · <count>  <detail>` — the last active call, as its tool row names it; the count only from the second call in a row |
| The model is streaming a call's arguments | `<tool title> · <count>  preparing…` — the title stays the tool's own, so the next call of a series only moves the count |
| Between steps: no call running or being prepared, no prose being written | `Thinking…` |
| Waiting for tool approval | `Waiting for tool approval` |
| The model is writing prose | no line: the note above shows it |
| A question to the user, a rate-limit wait | no line: they have their own cards |

- **Series.** Calls of the same tool in a row are one series: the title stays and only the count and the detail change. A note from the model or any other tool starts a new series. The count covers the turn's calls since its last note (`ToolCalls` of its assistant messages), plus the calls running and the one being prepared.
- **Hold.** Each state stays up at least 600 ms. What arrives meanwhile waits; when the time is up the latest is shown, skipping those in between. A series that flashed by during a pause is still shown, with its count, rather than skipped for the pause.
- **Thinking.** `Thinking…` appears only after 400 ms without a call: between two calls in quick succession it would flash for a frame.
- **Morph.** A change morphs instead of replacing the text (`js/textMorph.js`): the part the old and new text share, character by character, stays put; the rest dissolves into the new text through a slight blur while its width eases to fit, so whatever follows on the line glides along. The row's `Working for …` morphs the same way on each tick, so only the digits that change move. Off under reduced motion.
- **Sheen.** The title of a call and `Thinking…` carry the in-progress sheen (see Notes window); a wait does not.
- **Height.** The line is exactly one line tall for the whole running turn, empty or not, so nothing under it moves; both morph layers share one grid cell and one baseline.
- **Assistive technology.** A visually hidden status names only the tool or the wait, so a count or a path changing is not read out; the visible text is hidden from it.

Blazor renders the line empty and puts its state in `data-activity-*` attributes (and the row's text in `data-morph`); the script owns the text, so the renderer never diffs it. The line shows content only for the states above and does not wait for the live text's reading delay. It is shown only while the turn is collapsed: expanded, the live tool rows say the same. The row itself does not name the running tool, which would say the same thing twice.

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

An expanded turn ends with a **Hide steps** control (`.turn-collapse-footer`), so a long turn can be folded from where the reader is. It mirrors the row it closes: it starts at the left edge of the column, uses the row's muted type, a `chevron-up` and the visible label, and a hairline after the label marks where the intermediate steps end. It stays in the flow and has no fill, border or shadow at rest. Collapsing from it keeps the content below in place.

It must not be confused with the jump to the latest message (`.messages-jump`, see [UX decisions](12-ux-decisions.md)), which is the opposite in every cue:

| | Hide steps | Jump to latest |
| --- | --- | --- |
| Belongs to | the transcript, one turn | the window |
| Position | in the flow, left edge of the column | floating, centred at the bottom of the feed |
| Look | muted text, no fill, hairline | accent tint, border, shadow, pill |
| Icon | `chevron-up`: folds content | `arrow-down`: moves the view |
| Label | `Hide steps` | `New message` or `Writing…` with a pulsing dot |
| Shown | whenever a turn is expanded | only when something new is below and the feed is not at the bottom |

### Branches leaving a folded step

A branch may start at an intermediate message. Folding the step would hide its branch picker, so while the turn is collapsed the picker of every such step is shown under the row (`.turn-forks`), in step order, with the hint `Branches start inside this turn · expand it to see where`. On the branch that left, that step is the last message of the turn, so the space under the row is where the lines part. Expanding the turn puts each picker back under its own step.

Rejected: expanding turns with branches automatically (it undoes the compact view, and expansion is not persisted), and a branch counter in the row (the row carries only time).

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
5. During generation the row shows `Working for …` and the growing time; the feed holds at most the configured number of live notes for the turn (see [Notes window](#notes-window)), and one activity line under them.
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
