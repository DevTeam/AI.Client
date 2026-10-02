# Chat widgets

Status: implemented.

Chat widgets are small panels about the open chat — what it holds, what it spent, what it changed
— in a column beside the conversation. This document is the reference for building one: how the
column behaves, how a widget looks, how the code is put together, and what each existing widget
decided and why.

Current widgets:

| Id | Title | Icon | Component | Shows |
| --- | --- | --- | --- | --- |
| `chat-usage` | Usage | `gauge` | `ChatUsageWidget` | Context window, tokens, cost and where they went |
| `chat-files` | Files | `diff` | `ChatFilesWidget` | Files changed, lines added and removed, links to review |
| `chat-tools` | Tools | `tool` | `ChatToolsWidget` | Tool calls, outcomes and most used tools |
| `chat-performance` | Performance | `timer` | `ChatPerformanceWidget` | Wall-clock vs active time, throughput and where request time was spent |
| `chat-subtasks` | Subtasks | `fork` | `ChatSubtasksWidget` | Delegated work the chat ran on another model: requests, tokens and share of the whole chat or the last turn |
| `chat-knowledge` | Knowledge | `book` | `ChatKnowledgeWidget` | Files and pages the assistant read on the visible branch, grouped by tool, with the most recent paths |
| `chat-timeline` | Timeline | `history` | `ChatTimelineWidget` | One row per turn on the visible branch: when it started, how long it ran, its requests and tokens, and the tools and files it touched |
| `chat-branches` | Branches | `git-branch` | `ChatBranchesWidget` | Every stored branch of the chat: title, depth, message count, child branches and head timestamp, with a click that switches the visible branch |

## UX

### The column

- **Opening.** The `panel-right` button in the workspace header, shown only while a chat is open,
  toggles the column (`aria-pressed` reflects it). The close button in the column header does the
  same. Whether the column is open is remembered on this device.
- **Part of the layout, not a drawer.** On a wide window the column takes its own grid track
  (`--widgets-width`, 20rem) and the conversation narrows to make room. Drawers — settings,
  review, archive — open over everything, the column included. At 900px and below there is no
  room to share: the column floats over the conversation from the right edge, under the drawers,
  `min(20rem, 100vw)` wide.
- **No chat, no column.** The column is rendered only while a chat is selected; it is not an
  empty panel waiting for one.
- **Choosing widgets.** The `sliders` button in the column header opens a menu with a checkbox per
  widget, its icon, title and one-line description. Showing a widget again also unfolds it.
  Escape or a click outside closes the menu. When every widget is hidden, the column says so and
  offers "Choose widgets" instead of standing empty.
- **Folding.** The title is a button that folds the widget to its header (`aria-expanded`). A
  folded widget still states the one figure that matters in its header (`Summary`), so a column
  of folded widgets reads as a status bar. The column header has a single button — `chevrons-up`
  when at least one visible widget is open, `chevrons-down` when every visible widget is folded —
  that collapses or expands every visible widget in one click (`aria-pressed` reflects the
  current direction). Hidden widgets keep their saved folded state: the button only acts on what
  is actually on screen, so bringing a hidden widget back into the column later still shows it
  the way it was left. The button is disabled while no widget is visible.
- **Hiding.** The `x` in a widget header hides it; it comes back through the menu. Like the drag
  handle, the button appears on hover or focus, and always on touch screens.
- **Reordering.**
  - By pointer: press the handle and drag. A press that moves less than 4px is a click. The lifted
    widget follows the pointer with a shadow, a 2px accent line shows where it lands, and the list
    scrolls when the pointer nears its edges. Dropping where it already was is no move.
  - By keyboard: focus the handle and press Alt+↑ or Alt+↓. Hidden widgets are not stepped over
    one by one; the person moves among what they can see.
  - Only the handle starts a drag, so the rest of the header stays clickable and a press never
    needs a long-press to tell a drag from a scroll.
- **Deep links into widgets.** Other UI can open the column straight at a widget. Pressing the
  context ring in the composer opens the column with Usage shown and unfolded, wherever the person
  put it (`ShowUsageWidgetAsync`).
- **Persistence.** Order, hidden and folded state are this device's preferences, kept with the
  other client settings. They survive reloads and new builds.

### Inside a widget

- **Most important first.** A widget leads with the figure people open it for — the context fill,
  the cost, the file count — and puts detail below it.
- **Scope.** Widgets whose figures can cover either the whole chat or the last turn start with the
  shared "Whole chat / Last turn" switch. Everything below it — headline, grid, breakdown, folded
  summary — follows the choice. The choice is the widget's own and starts at "Whole chat". The
  switch is hidden while the chat has nothing to count at all, so an empty widget is just its
  empty-state line.
- **The turn in progress.** A running turn counts as the last turn, and its figures grow as it
  goes. The headline says that they are not final ("so far"); the footer does not repeat it.
- **Empty states.** Every widget says what will appear and when: "Token usage appears here once
  the chat sends its first request." In the last-turn scope a widget with data elsewhere says what
  the last turn did not do: "The last turn changed no files."
- **Honesty about numbers.** Figures the endpoint did not report are marked "≈"; a cost that
  covers only some requests says "partial"; one worked out from other quotes says "estimated".
  Why is explained in a tooltip, not in another row.
- **Leave out what is empty.** A row with nothing to show is not printed as zero: no reasoning row
  without reasoning, no cache-reset row when nothing reset the cache.
- **Actions where the figures are.** A widget offers the next step where it shows the problem:
  "Compact now" beside a full context window, "Set prices" in place of an unknown cost, "Review"
  beside changed files.

### Accessibility

- The column is an `aside` labelled "Chat widgets"; each widget is a `section` labelled by its
  title.
- Fold and menu buttons carry `aria-expanded`; the scope switch is a `radiogroup` of `radio`
  buttons with `aria-checked`.
- Meaning never rests on colour alone. Counts carry `+` and `−`; file kinds carry the letters
  A, M, D and R; bars are decoration (`aria-hidden`) beside numbers that say the same, or carry an
  `aria-label` of their own when they are the only statement.
- Everything that can be done by pointer can be done by keyboard, reordering included.
- Motion is short and optional. With reduced motion the column works the same, and nothing that
  explains a figure depends on an animation.

## Design

### Shell

Every widget sits in `ChatWidget`, so the column reads as one set:

```
┌──────────────────────────────────────────┐
│ ⠿  ◔ Usage   16% · $0.18           ⌄   ✕ │  header: handle, icon, title, folded summary, chevron, hide
├──────────────────────────────────────────┤
│  body (.chat-widget-body)                │  .1rem .85rem .85rem padding, .78rem text
└──────────────────────────────────────────┘
```

- The card has a 1px `--color-border-subtle` border, a radius of `.75rem × --corner-scale`, and
  `--color-surface` on the column's `--color-bg-sunken`. Widgets are .6rem apart.
- Header: 2.3rem high; the title is .8rem, weight 500; the folded summary is regular weight in
  `--color-text-subtle` with tabular digits and ellipsis.
- The chevron points down when unfolded and right when folded.

### Body

The body is a stack of sections (`chat-usage-section`): a 1px rule between them, .7rem padding,
.4rem gap inside. Text sizes, from largest to smallest:

| Size | Use |
| --- | --- |
| 1.35rem, 500 | One headline count (`chat-widget-count`: files, calls, reads, turns, branches, subtask requests, wall-clock) |
| .86–.9rem, 500 | Headline figures (Usage: `154k → 7k` and cost; Files: `+307 −76`; `chat-widget-secondary`: tok/s, subtask share) |
| .78rem | Body text and rows |
| .74–.76rem | Grids, list figures, the scope switch, inline links |
| .7–.72rem | Legends, column headers, footnotes, `small` qualifiers |

Figures always use tabular digits (`font-variant-numeric: tabular-nums`) and do not wrap.

### Building blocks

| Block | Classes | Looks like |
| --- | --- | --- |
| Scope switch | `chat-widget-scope` (component `ChatWidgetScopeSwitch`) | Two equal segments on `--color-fill`; the chosen one raised on `--color-surface` with a hairline ring |
| Heading row | `chat-usage-row chat-usage-heading` | Label left, figure right, baseline aligned |
| Headline | `chat-usage-row chat-usage-headline` | The scope's main figures, .9rem 500, qualifiers in `small` (Usage) |
| Count headline | `chat-widget-headline`, `chat-widget-count`, `chat-widget-headline-aside`, `chat-widget-secondary` | Every other widget: one large count and its muted unit, an optional smaller figure pushed to the right edge |
| Fact grid | `chat-usage-grid` (a `dl`) | Two label–value pairs per line: `auto 1fr auto 1fr`; labels subtle, values right-aligned; a long value takes a row of its own (`chat-usage-grid-row` + `chat-usage-grid-wide`) |
| Bar | `chat-usage-bar`, `chat-files-bar` | .35–.45rem high on `--color-fill`, segments `flex-grow` by value |
| Legend | `chat-usage-legend`, `context-swatch` | A wrapping line of swatch, label and value |
| Share bars | `chat-usage-shares` | Label, 0.3rem track, percentage |
| List row | `chat-files-row` | A button: grid `1rem 1fr 2.6rem 2.6rem`, with a 3px bar on the second line |
| Inline link | `chat-usage-link` | Accent text, underline on hover |
| Action button | `compact-action` | The small secondary button used across the app |
| Note | `chat-usage-note`, `chat-usage-footnote`, `chat-usage-empty` | A tinted strip for warnings; subtle .72rem text for notes and empty states |

### Colour

Only theme variables, never literal colours, so every theme and both modes work:

- text: `--color-text`, `--color-text-secondary`, `--color-text-subtle`, `--color-text-faint`;
- surfaces: `--color-surface`, `--color-fill`, `--color-fill-hover`, `--color-border-subtle`;
- meaning: `--color-success` (added), `--color-danger` (removed, critical), `--color-warning`
  (filling up), `--color-accent-text` (links). Context layers use their `context-layer-*`
  colours.

Colour carries meaning only together with a sign, a letter or a word. A zero count is a muted
"—", not a coloured one.

### Alignment

Numbers compared down a list sit in fixed-width columns, centred, so they line up file under file;
a bar that shows the same values goes on the second line of the row, under the name, instead of
competing with the numbers. Column headers ("added", "removed") sit over their columns.

### Copy

English, sentence case, no terminal punctuation on labels. Units follow the number ("110 tok/s",
"3 files"). Plurals are spelled out ("1 turn", "4 turns"). Tooltips explain how a figure was
made; they do not repeat it. Attach explanations to values themselves, including headline counts,
grid values, list counts and legend figures. Use `title`, which the shared `tooltips.js` turns into
an `app-tooltip`; widgets do not create their own tooltip component. For abbreviated token counts,
include the unabridged count via `IUsagePresentation.FormatExact`. Explain the selected scope,
estimates and aggregation rules where they affect the meaning (for example, repeated file changes
count again in line totals, while a file path counts once). Folded summaries also expose the
widget description and full summary through a tooltip.

## Architecture

### Pieces

| Piece | File | Role |
| --- | --- | --- |
| `ChatWidgetDefinition` | `src/AI.Web/Widgets/ChatWidgets.cs` | Id, title, icon, description of a kind of widget |
| `IChatWidgetCatalog` | same | Every widget this build has, in the order a new column shows them |
| `ChatWidgetPreference` | same | One widget as the person left it: `Id`, `Hidden`, `Collapsed` |
| `IChatWidgetLayout` | same | `Arrange` saved preferences against the catalog; `Move`, `MoveBy`, `Update`; `SetCollapsed` for the column-wide toggle |
| `ChatWidgetContext` | same | What the column hands a widget: definition, preference, `ToggleCollapsed`, `Hide`, `Move` |
| `ChatWidgetScope` | same | `Chat` or `LastTurn` |
| `ChatWidgetRail` | `src/AI.Web/Components/ChatWidgetRail.razor` | The column: header, menu, list, reordering, empty state |
| `ChatWidget` | `src/AI.Web/Components/ChatWidget.razor` | The shell: handle, folding title, `Summary`, `HeaderActions`, hide |
| `ChatWidgetScopeSwitch` | `src/AI.Web/Components/ChatWidgetScopeSwitch.razor` | The shared scope switch (`Value` / `ValueChanged`) |
| `chatWidgets.js` | `src/AI.Web/wwwroot/js/chatWidgets.js` | Pointer reordering by the handle |
| Client settings | `src/AI.Web/Settings/ClientSettings.cs` | `ChatWidgetsOpen`, `ChatWidgets` |

### Data flow

```
ClientSettings ──► Home._chatWidgets ──Arrange──► ChatWidgetRail ──ChatWidgetContext──► widget
      ▲                                               │
      └──────────── SaveChatWidgetsAsync ◄── WidgetsChanged (fold, hide, move, drop)

Home (selected chat, visible branch, run snapshot, usage store) ──parameters──► widget
widget ──EventCallback──► Home (open review, compact, open connections, new chat)
```

- **`Home` owns the state.** It loads preferences at start (`WidgetLayout.Arrange(settings.ChatWidgets)`),
  passes the list to the rail, and saves what the rail reports back. The rail never writes
  settings itself; it computes the new list with `IChatWidgetLayout` and raises `WidgetsChanged`.
- **The rail owns the arrangement, the widget owns its content.** The rail renders
  `WidgetTemplate` for every shown widget with a `ChatWidgetContext`. `Home` supplies the template:
  a `switch` on `widget.Definition.Id` that renders the matching component with its data.
- **Widgets get data, not services that fetch it.** The selected chat, its visible branch, the
  run snapshot and the usage store already live in `Home` and change with the conversation.
  Passing them as parameters keeps a widget in step with what is on screen and keeps it testable.
  Usage is refreshed when the column opens and when another chat is selected
  (`RefreshChatUsageAsync`).
- **Figures come from services.** Anything worth testing — sums, grouping, ordering — lives behind
  an interface in `src/AI.Web/Widgets` or `src/AI.Web/Usage`, bound in `Composition.cs`
  (`RootBind<IX>().To<X>()`), and the component injects it. Components hold only presentation and
  the widget's own UI state, such as its scope or "show all".
- **Actions go back up.** A widget never opens drawers or calls APIs on its own; it raises an
  `EventCallback` and `Home` acts, so a review opened from a widget behaves exactly like one opened
  from the transcript.

### Saved preferences and builds

`Arrange` reconciles saved preferences with the catalog on every start: unknown ids are dropped,
duplicates collapse, widgets new to the catalog join at the end, and the person's order of the rest
is kept. Therefore:

- an id is a stable storage key — rename a widget by changing `Title`, never `Id`;
- removing a widget from the catalog is safe; its saved entry disappears on the next save;
- the catalog order only matters to people with no saved order.

### Reordering

`ChatWidgetRail` imports `chatWidgets.js` after first render and attaches it to the list with a
`DotNetObjectReference`. The script uses pointer events with pointer capture on the handle (not
HTML5 drag and drop, for touch support and a custom drop line), moves the lifted widget with a
transform, and on drop calls `OnWidgetDropped(id, beforeId)`. The rail turns that into
`Layout.Move` and raises `WidgetsChanged`; Blazor re-renders the list in the new order. The
module is disposed with the rail; `JSDisconnectedException` is ignored on teardown.

### Dependency rules

- Widget code follows the project conventions: dependencies through interfaces and Pure.DI,
  records for data, no static state.
- `AI.Web` depends only on `AI.Contracts`; a widget needing new data gets it into a contract
  first, then into `Home`.

## Adding a widget

1. **Catalog.** Add a constant and a definition to `ChatWidgetCatalog`:

   ```csharp
   public const string ChatFiles = "chat-files";

   new(ChatFiles, "Files", "diff", "Files the open chat changed, with lines added and removed")
   ```

   The icon is an `AppIcon` name; the description is the line in the show/hide menu.

2. **Service.** Put the figures behind an interface in `src/AI.Web/Widgets`, bind it in
   `src/AI.Web/Composition.cs`, and unit-test it in `tests/AI.Web.Tests/Widgets`.

3. **Component** in `src/AI.Web/Components`:

   ```razor
   @inject IMyCalculator Calculator
   <ChatWidget Widget="@Widget" Summary="@GetSummary()" Class="my-widget">
       <ChatWidgetScopeSwitch Value="_scope" ValueChanged="SetScope" />
       ...
   </ChatWidget>

   @code {
       [Parameter, EditorRequired]
       public ChatWidgetContext Widget { get; set; } = null!;

       private ChatWidgetScope _scope = ChatWidgetScope.Chat;

       protected override void OnParametersSet() => Refresh();

       private void SetScope(ChatWidgetScope scope)
       {
           _scope = scope;
           Refresh();
       }
   }
   ```

   Compute once per parameter or scope change, not in every expression that renders. Return a
   short `Summary`, or null when there is nothing to say.

4. **Template.** Add a case to the `WidgetTemplate` switch in `src/AI.Web/Pages/Home.razor`.
   `widgetChat` is the selected chat and `widgetRun` its run, if any. Use the visible branch
   (`Feed.BuildBranch(widgetChat.Messages, _branchLeafId)`), not every message of the chat, so the
   figures follow the branch being read.

5. **Styles** in `src/AI.Web/wwwroot/css/app.css`, next to the other widget rules. Reuse the
   building blocks above; add widget-specific classes with the widget's prefix (`chat-files-*`).

6. **Check it** in a running Host: folded and unfolded, both scopes, with data and empty, during a
   run, in light and dark themes, and at 900px or less. Add it to the table at the top of this
   document.

## Usage widget

Context window, tokens and cost (`ChatUsageWidget`).

- **Context.** One heading line — "Context · connection" on the left, `used / capacity  percent`
  on the right, the percentage coloured when filling up — then the layer bar, the legend with
  free space, the provider's limits when stated, and the notes and actions for a filling window
  ("Compact now", "New chat", "Compact earlier turns", "Undo" of a summary). The context section
  ignores the scope: it is always the window as it stands. It is hidden when context usage is
  switched off.
- **Scoped figures.** Headline `input → output` and cost (with "partial" or "estimated", or "Set
  prices" when unknown). Then the grid: Turns (whole chat only), Requests, Cached (with the
  achievable share when the cache served at least 20 points less than it could have), Reasoning,
  Speed, and Cache reset on a row of its own. Then the shares by purpose when there is more than
  one, and the "≈ estimated" footnote.
- **Last turn** is the run's live turn while it is newer than the stored one, otherwise the last
  stored turn (`GetTurn`). Requests outside any turn — naming the chat, reply suggestions — count
  in the whole chat only.
- **Folded summary:** context percentage and the scope's cost, or its token flow without prices.
- Data: `ComposerContext` from `IComposerContextPresentation`, `ChatTokenUsage` from the usage
  store, `TurnTokenUsage` from the run snapshot; formatting by `IUsagePresentation`. See
  [Token usage](28-token-usage.md).

## Files widget

Files changed by the chat (`ChatFilesWidget`, `IChatFileStatisticsCalculator`).

- **Source of truth.** Each completed run saves a receipt (`WorkspaceChanges` on its message): the
  net change of that run against content captured before it first touched each path. The widget
  adds the receipts of the visible branch.
- **Sum of edits, not net change.** A file edited in several turns appears once, with its edits
  summed: a line written in one turn and removed in the next counts on both sides. A true net
  change since the chat began would need a snapshot taken when the chat started, which does not
  exist. The total's tooltip says so.
- **Kind across turns.** Deleted if its last change deleted it; otherwise Added if its first change
  created it; otherwise Renamed if any change renamed it; otherwise Modified. A rename carries the
  file's earlier edits to its new path and keeps the original path for the tooltip.
- **Running turn.** The run snapshot's changes are added to the last turn until the receipt
  arrives; once the transcript has it (`LastTurnHasWorkspaceReceipt`) the live copy is ignored, so
  nothing is counted twice. Files changed only by the running turn cannot be reviewed yet and are
  not clickable.
- **Layout.** Scope switch; file count with "+N −M" and the added/removed bar; counts by kind
  (A new, M edited, D deleted, R renamed); "Most changed" — files by lines touched, binary files
  last, six shown with "Show N more"; then "In X of Y turns" and the review button.
- **Review.** A review covers one turn's receipt. A file opens the review of the newest turn that
  changed it, unfolded and scrolled to that file (`ChatFileReviewRequest`, `ReviewWorkspace.StartPath`).
  The button opens that turn's review — "Review" when the scope has one receipt, "Review latest"
  when it has several. A review of the whole chat would need a combined diff on the server.
- **Folded summary:** `files · +added −removed` for the chosen scope.

## Tools widget

Tool use on the visible branch (`ChatToolsWidget`, `IChatToolStatisticsCalculator`).

- **Scope.** Whole chat or last turn, split at user messages. Counts describe calls in this
  branch's transcript; calls inside subtasks are not included unless recorded there.
- **Source.** Assistant `ToolCalls`, paired with tool messages by `ToolCallId` within each turn.
  Repeated snapshots of the same call id count once; repeated uses with distinct ids count separately.
  Active invocations join the last turn only when the snapshot's `ActiveMessageId` is in it.
  Stored results take precedence over active invocations, avoiding double counting on completion.
- **Outcomes.** Succeeded and Errors follow `ToolResultIsError`, retained in the compact transcript
  even when the full output is omitted, or the stored result decoded through `IToolResultCodec`;
  refused calls count as errors. Active calls say Running; other calls in a live turn say
  Awaiting result. Ended calls without a result say No result. Incomplete or unrecognized legacy
  results, and omitted results without an error status, say Unknown result. Empty outcome rows are hidden.
- **Layout.** Scope switch; total calls and distinct tools; outcome grid; Most used, sorted by
  call count then name, six shown with Show more. Each tool shows its server, count, relative
  usage bar and errors when present. The raw call name is in the tooltip; different servers
  remain separate. In the whole-chat scope the footer shows turns with calls.
- **Folded summary.** Call count and errors for the selected scope.
- **Live data.** The headline says so far during a running or paused turn. Draft calls with
  arguments still streaming are not counted until recorded or executing. No durations are inferred
  from message timestamps, which do not measure tool execution time.

## Performance widget

Latency, throughput and where request time was spent (`ChatPerformanceWidget`,
`IChatPerformanceCalculator`).

- **Scope.** Whole chat or last turn, with the same switch as the other widgets. The running turn
  is treated like any last turn: its figures grow as it goes, and the widget says "so far" while
  it does. The folded summary shows the scope's throughput and wall-clock when one is known.
- **Source of truth.** Provider-reported figures come from the chat's stored usage
  (`ChatTokenUsage`) for the whole chat, and from the run snapshot's `TurnUsage` for the last
  turn — the same `ResolveTotals` rule the Usage widget uses, so the two widgets agree. Wall-clock
  and idle are worked out from message timestamps and are flagged approximate: a leading "≈" sits
  in front of them and the tooltip says so ("Whole time from the first user message to the latest,
  including pauses while you were not in the chat").
- **What it shows.**
  - **Headline — wall-clock.** "11 min ≈ wall-clock" — the time from the first user message to
    the latest. When the branch has no wall-clock span, the headline shows active time instead
    ("4 min active").
  - **Throughput.** Output tokens per second for the chosen scope, computed through
    `IUsagePresentation.OutputSpeed` so the widget honours the same "too little was measured"
    guard as Usage. The speed figure carries a plain `tok/s` label and lives on the right of the
    headline; nothing is inferred from message timestamps.
  - **Phases.** Where the active time went, as a stacked bar and a legend. Generation is the
    output tokens at the scope's output speed; Reasoning is the reasoning tokens at the same
    speed; Other is whatever is left inside the active window. The bar is hidden when there is
    only one phase (a single timed request with no reasoning and no remainder). Each segment
    has a label in the legend with its duration, so the bar never stands alone.
  - **Meta rows.** Active (the sum of request durations, when the headline shows wall-clock),
    Idle (wall-clock minus active), Requests, Output, and Reasoning when there is any. Idle keeps
    the "≈" qualifier; the others are provider figures.
  - **Footer.** "In X turns" for the chosen scope.
- **Honesty about numbers.**
  - Provider-reported figures (request duration, throughput, token counts) are quoted as they
    came in. They are the only ones the widget treats as billed.
  - Wall-clock and idle are always marked approximate: they include whatever time passed between
    two messages, which is not the model thinking.
  - No durations are inferred from message timestamps for tool execution, the same note the Tools
    widget carries. The phases bar is built from output speed and the token counts the provider
    reported, never from message spans.
- **Folded summary.** `28 tok/s · ≈ 11 min` for the chosen scope, or the throughput alone when
  wall-clock is the same as active. Null when nothing can be said.
- **Data.** `ChatTokenUsage`, `TurnTokenUsage` and the visible branch's `ChatMessageView`; shared
  formatting in `IUsagePresentation`. No new presentation service — durations are formatted inside
  the widget, and only one rule (`ms → "N s" / "N min" / "N h"`) needed to live there.
- **Architecture.** A `IChatPerformanceCalculator` in `src/AI.Web/Widgets` takes the same pair
  the Usage widget reads from Home (`ChatTokenUsage?` and `TurnTokenUsage?`) plus the visible
  branch and returns a `PerformanceStatistics` record that the widget renders. The calculator is
  bound in `Composition.cs` and tested in `tests/AI.Web.Tests/Widgets`. The widget itself is a
  `ChatWidget` like the others, with `ChatWidgetScopeSwitch`, `Summary`, and the same
  `chat-usage-*` building blocks and the shared count headline — only the phase bar uses
  widget-specific classes (`chat-performance-*`).

## Subtasks widget

Delegated work the chat ran on another model (`ChatSubtasksWidget`,
`IChatSubtaskStatisticsCalculator`).

- **Scope.** Whole chat or last turn, with the same switch as the other widgets. The running
  turn says "so far", just like Usage and Performance.
- **Source of truth.** The provider's usage ledger carries a `Subtask` slice on the chat and on
  each turn (`ChatTokenUsage.ByPurpose`, `TurnTokenUsage.ByPurpose`). That is the only honest
  source: the transcript does not record how many distinct subtasks ran, how each one finished
  or how long it took, and the widget does not invent those figures.
- **What it shows.**
  - **Headline.** Number of subtask requests in the scope, with the share of the scope's tokens
    that went to them on the right.
  - **Token breakdown.** Input, output, and reasoning (a muted row only when reasoning is
    non-zero, so an empty breakdown stays out of the way). All three come from the same
    `Subtask` slice.
  - **Footer.** "In N of M turns delegated work".
- **Honesty about numbers.**
  - Provider-reported figures (requests, input, output, reasoning) are quoted as they came in.
  - The share is `(subtask input + subtask output) / (scope input + scope output)`, not a
    fraction of requests. Null when the scope used nothing and the share would divide by zero.
  - Number of distinct subtasks, outcomes per subtask and wall-clock per subtask are not in the
    ledger and are not shown.
- **Folded summary.** `N requests · M%` when there is data; null when the scope used nothing.
- **Data.** `ChatTokenUsage`, `TurnTokenUsage` and the visible branch's `ChatMessageView`;
  formatting by `IUsagePresentation`. The widget injects the same presentation service Usage
  does, so token counts read identically across widgets.
- **Architecture.** `IChatSubtaskStatisticsCalculator` in `src/AI.Web/Widgets` takes the same
  pair the Usage and Performance widgets read from Home (`ChatTokenUsage?` and `TurnTokenUsage?`)
  plus the visible branch and the running flag, and returns a `SubtaskStatistics` record. It
  uses the same chat-vs-turn rule the Performance widget uses (`ChooseTurn`), so the three
  widgets agree on what the last turn is. Bound in `Composition.cs` and tested in
  `tests/AI.Web.Tests/Widgets`. The widget itself is a `ChatWidget` with `ChatWidgetScopeSwitch`,
  `Summary`, the same `chat-usage-*` building blocks and the shared count headline; it has no
  widget-specific classes.

## Timeline widget

One row per turn on the visible branch (`ChatTimelineWidget`,
`IChatTimelineStatisticsCalculator`).

- **Scope.** Always the whole branch — the timeline of one turn is that turn, so the shared
  "Whole chat / Last turn" switch is not shown. The empty state says so: "Turns appear here once
  the chat sends its first message."
- **Source of truth.** Two sources, aligned by position. Per-turn figures (requests, tokens)
  come from the provider usage ledger's `ChatTokenUsage.Turns` slices, matched to branch turns
  oldest first; the latest slice can be replaced by `TurnTokenUsage` when the run is on its last
  turn and the live slice starts no earlier than the stored one (the same rule Performance and
  Subtasks use, so the widgets agree). Durations and timestamps come from message
  `CreatedAt` and so are marked "≈" in markup, the same way Performance marks wall-clock.
- **What it shows.** One row per turn, newest last. Only the newest eight are shown; older ones
  sit behind "Show N earlier turns" at the top of the list:
  - **Head.** Turn index (`Turn N`), local start time (`HH:mm` with full timestamp in the
    tooltip), duration when the turn has more than one message (`≈ N s` / `min` / `h`), and
    `running` while the last turn is the live one. The opening assistant turn (before the first
    user message) has no duration and its label is muted.
  - **Figures.** Requests (`N request(s)`) and input → output tokens when the provider reported
    them. Missing slices say "No usage reported" instead of a row of zeroes.
  - **Chips.** Distinct tool call names in the order they first appeared (server prefix stripped,
    same form the Tools widget uses), then the distinct files changed by the turn, taken from
    saved receipts. A file chip shows the file name; the full path is in its tooltip.
  - **Action.** A click anywhere on the row scrolls the feed to the turn's first user message via
    `OnSelectTurn`. The row's head is a button, so the keyboard reaches each turn too; the opening
    turn has no anchor, so its head is disabled and its row does not react.
- **Honesty about numbers.**
  - Provider-reported figures (requests, input, output) are quoted as they came in. A turn with
    no ledger slice shows nothing rather than a placeholder.
  - Durations and timestamps are derived from message timestamps; they include whatever time
    passed between two messages, which is not the model thinking. The "≈" marker and the tooltip
    say so.
  - Tool call names use the part a person recognises (after `__`), not the raw
    `mcp_server__tool` id.
- **Folded summary.** `N turn(s)` when there are any, `Turn N running` while the last one is.
- **Data.** `ChatTokenUsage`, `TurnTokenUsage`, the visible branch's `ChatMessageView`, and the
  running flag. Token formatting is shared with Usage through `IUsagePresentation`. Duration and
  time formatting live inside the widget — one rule each, both tiny.
- **Architecture.** `IChatTimelineStatisticsCalculator` in `src/AI.Web/Widgets` returns a
  `ChatTimelineStatistics` record holding one `TimelineTurn` per branch turn. Bound in
  `Composition.cs` and tested in `tests/AI.Web.Tests/Widgets`. The widget itself is a
  `ChatWidget` with `Summary` and the same `chat-usage-*` building blocks; only the vertical
  timeline layout (rail, dot, chips) uses widget-specific classes (`chat-timeline-*`). Clicking
  a row raises `OnSelectTurn(messageId)` and `Home.ScrollToTurnAsync` forwards it to
  `MessageFeed.ScrollToMessageAsync`, so the timeline doubles as a table of contents.

## Branches widget

The branches the chat actually has, with everything a person needs to pick one without going back
to the sidebar: title, depth, how many messages it owns, how many child branches it has, and when
its head message was recorded. The widget is also the only place that shows a branch's depth at a
glance and lets a person jump to it with one click from the column — the sidebar already lists
branches, but only here do they line up with the rest of the column's widgets and stay in scope
while the transcript is being read.

- **Scope.** Chat only, no scope switch. A single branch never has sub-branches of its own, so
  "branches of the last turn" would always be empty; "branches of the chat" is what the page
  already means by the open chat.
- **Source of truth.** Three stored values: `ChatDetails.Branches` (the list),
  `ChatMessageView.CreatedAt` (for head timestamps and message-count walks), and
  `ChatMessageView.ParentId` (to walk the message tree from each branch's root). Anything that
  cannot be inferred from those three — branch-local token accounting, tool execution time on the
  branch, TTFT — is left out and the widget does not pretend to know it. If a provider later
  reports branch-scoped usage, the calculator gains a new field; the widget's contract stays the
  same.
- **What's shown.** For every stored branch:
  - **Title** from `ChatBranchView.Title`, falling back to `Branch` when the stored title is blank
    (a cycle in the parent chain falls back too, with a finite depth).
  - **Message count** = how many messages walk from the branch's root through `ParentId`. The
    root message itself counts, and so do descendants that have not yet been given their own
    branch.
  - **Child branches** = how many other stored branches have this one as `ParentBranchId`. A
    branch with no children is not a dead end — it can still own messages that have not been
    forked yet.
  - **Head timestamp** from the head message's `CreatedAt`. The UI marks it with "≈" and the
    tooltip says so: a long idle pause before the last message counts as part of the branch's age
    rather than the user's, the same convention Timeline and Performance use.
  - **Active state**: the row whose id matches the currently visible branch gets the same accent
    bar the sidebar uses for a selected chat nav row, and `aria-current="true"` so screen readers
    announce it.
- **Honesty rules.**
  - Head timestamps come from message `CreatedAt`, not from a provider timestamp, and so are
    marked approximate. The footer states this in plain text.
  - A branch whose head message is missing is reported with zero messages and a `null` head
    timestamp — it is not silently dropped, and the row is still rendered so the user sees the
    branch exists.
  - A cycle in `ParentBranchId` is walked with a visited set so depth stays finite; the row is
    still listed, with the depth the walker reached.
- **Empty state.** "Branches appear here once a branch is created." — the chat itself is not a
  branch, and an empty `Branches` list means none have been forked yet.
- **Folded summary.** `N branches` (`N branch` when there is only one).
- **Interactivity.** Each row is a button. Clicking it raises
  `OnSelectBranch(ChatBranchSummary)` and `Home.OnWidgetBranchSelectedAsync` adapts it to a
  `BranchTreeItem` and calls the same `SelectBranchTreeItemAsync` the sidebar's branch links use,
  so the two surfaces switch branches through one code path.
- **Data.** `ChatDetails.Branches`, `ChatDetails.Messages`, and the id of the currently visible
  branch (`Home.GetSelectedBranchId()`). No scope switch, no live turn, no token ledger.
- **Architecture.** `IChatBranchesStatisticsCalculator` in `src/AI.Web/Widgets` returns a
  `ChatBranchesStatistics` record holding one `ChatBranchSummary` per stored branch. Bound in
  `Composition.cs` and tested in `tests/AI.Web.Tests/Widgets`. The widget itself is a `ChatWidget`
  with `Summary` and the shared `chat-usage-*` building blocks (`chat-usage-empty`,
  `chat-usage-section`, `chat-usage-footnote`); the row layout, depth indent, and active accent
  use widget-specific classes (`chat-branches-*`) and reuse the sidebar's `.branch-tree-line` for
  the elbow guide on child branches (depth 0 draws none), so a branch row in the widget and a
  branch row in the sidebar read as the same shape. The row is a two-column grid (icon, body); the
  guide is absolutely positioned and so takes no grid column. Meta items never wrap inside
  themselves and are separated by a CSS `·`, so a narrow panel moves whole items to the next line.
