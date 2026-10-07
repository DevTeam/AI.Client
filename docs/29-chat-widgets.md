# Chat widgets

Status: implemented.

Chat widgets are small panels about the open chat — what it holds, what it spent, what it changed
— in a column beside the conversation. This document is the reference for building one: how the
column behaves, how a widget looks, how the code is put together, and what each existing widget
decided and why.

Current widgets:

| Id | Title | Icon | Component | Shows |
| --- | --- | --- | --- | --- |
| `chat-schedule` | Schedule | `stopwatch` | `ChatScheduleWidget` | In an ordinary chat, the way to schedule it; in a scheduled chat, the countdown to the next run, its state, Run now, Pause or Resume, Edit and Remove, what the schedule does and the recent runs, each opening its branch — see [Scheduled chats](35-scheduled-chats.md) |
| `chat-context` | Context | `chart-pie` | `ChatContextWidget` | How the context window of the next request is filled: the ring, its layers and what is left |
| `chat-usage` | Usage | `gauge` | `ChatUsageWidget` | Tokens, cost and where they went, with the compacting actions for a filling window |
| `chat-files` | Files | `diff` | `ChatFilesWidget` | Files changed, lines added and removed, links to review |
| `chat-tools` | Tools | `tool` | `ChatToolsWidget` | Tool calls, outcomes and most used tools |
| `chat-performance` | Performance | `timer` | `ChatPerformanceWidget` | Wall-clock vs active time, throughput and where request time was spent |
| `chat-subtasks` | Subtasks | `fork` | `ChatSubtasksWidget` | Delegated work the chat ran on another model: requests, tokens and share of the whole chat or the last turn |
| `chat-knowledge` | Knowledge | `book` | `ChatKnowledgeWidget` | Files and pages the assistant read on the visible branch, grouped by tool, with the most recent paths |
| `chat-timeline` | Timeline | `history` | `ChatTimelineWidget` | One row per turn on the visible branch: when it started, how long it ran, its requests and tokens, and the tools and files it touched |
| `chat-branches` | Branches | `git-branch` | `ChatBranchesWidget` | Every stored branch of the chat: title, depth, message count, child branches and head timestamp, with a click that switches the visible branch |
| `chat-references` | References | `link` | `ChatReferencesWidget` | The files, directories, images, uploads, skills, diffs, chats and projects the messages named, added up over the whole chat or the last turn, each with a click that shows its earliest message |
| `chat-models` | Models | `cpu` | `ChatModelsWidget` | Which models answered the chat and how many answer requests each served, for the whole chat or the last turn |
| `chat-team` | Team | `users` | `ChatTeamWidget` | The branches that sent team messages into the visible branch, with what each sent: messages, asides and intents, for the whole chat or the last turn, each with a click that shows its latest message |
| `chat-unfinished` | Unfinished work | `list-checks` | `ChatUnfinishedWidget` | Every run in this project that has not finished — waiting for an approval or an answer, paused, interrupted, failed with a recovery action left, still generating or with messages queued behind it — with what each waits on, a filter by reason, a click that opens its chat and branch and, where a resume is what it waits for, a button that resumes it |

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
- **Deep links into widgets.** Other UI can open the column straight at a widget. The widget menu
  and the navigation targets (`widgets.<id>`) are the ways in; the composer keeps no link of its
  own, so a click near Send never moves the column.
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

## Context widget

The context window of the next request (`ChatContextWidget`).

- **Where it came from.** The send button used to carry these figures in its tooltip, where they
  were readable only while the pointer rested on the button. The setting that switched them on and
  off is gone: the widget shows them always, and the ring around Send stays visible with them.
- **What it shows.** The same block the tooltip carried — the ring the composer draws, at 92px,
  with the used percentage and `used / capacity` in its centre, then one legend row per layer with
  its swatch and tokens, "Free" last, and the presentation's note for a window that is filling up
  or was compacted — laid out beside the ring instead of inside a tooltip.
- **Unknown layers.** Until the branch's first measured request only the draft and the reserves are
  known; the layers the Host measures read as a muted "—" rather than as an empty 0, using the same
  rule the tooltip used. Each value carries a tooltip of its own: the ring's percentage says what
  the window includes, `used / capacity` gives the unabridged counts and where the capacity comes
  from, every layer row names its tokens and why its space is held, and "Free" says what is left
  after the reserved space. An unknown layer says it is not measured yet instead of repeating "—".
- **No scope.** The window is the window as it stands, so the widget has no "Whole chat / Last
  turn" switch. Its folded summary is the percentage.
- **Empty state.** "Context figures appear here once a chat is open." The widget is rendered only
  while a chat is selected, so this is the state of a chat without a composer context rather than
  the usual one.
- Data: `ComposerContext` from `IComposerContextPresentation`, formatting and the note by the same
  presentation. The fills, the compacting actions and the provider's limits stay in the Usage
  widget. See [Token usage](28-token-usage.md).

## Usage widget

Tokens and cost (`ChatUsageWidget`).

- **Context.** One heading line — "Context · connection" on the left, `≈used / capacity  percent`
  on the right, the percentage coloured when filling up — then the layer bar, the legend with
  free space, the provider's limits when stated, and the notes and actions for a filling window
  ("Compact now", "New chat", "Compact earlier turns", "Undo" of a summary). The context section
  ignores the scope: it is always the window as it stands. The Context widget shows the same window
  as a ring; this section keeps the compacting actions that go with it.
- **Scoped figures.** Headline `input in → output out` and cost (with "partial" or "estimated", or "Set
  prices" when unknown). The flow is cumulative traffic, not a compression ratio. Then the grid:
  Turns (whole chat only), Requests, measured Cached, approximate Prefix overlap when comparable
  estimates are recorded, Reasoning, Speed, and Prefix changes on a row of its own. Overlap
  describes application stability; it does not predict cache eligibility or expiry. Legacy
  records without a denominator are excluded from overlap. Then the shares by purpose when there is more than
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
  call count then name, six shown with Show more. A tool's label comes from `IToolPresentations`,
  so a dedicated adapter names it here exactly as the message feed does and an unknown tool is
  only reformatted from its call name. Each tool shows its server, count, relative
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

## References widget

What the chat was given and received (`ChatReferencesWidget`,
`IChatReferenceStatisticsCalculator`).

- **Scope.** Whole chat or last turn, with the same switch as the other widgets; the running turn
  says "so far". A turn starts at a person's message, the same split Performance, Files and Tools
  use, so the widgets agree on what "the last turn" is.
- **Source of truth.** `ChatMessageView.Resources` — the references the transcript stores beside
  the message that named them, on the visible branch. Nothing is parsed out of the message text:
  a path written in prose is not a reference and is not counted. The widget says so in its tooltip.
- **What counts as one reference.** Two resources are the same target when a person would call
  them so: the same path; for a file, the same line range, since a message about lines 12-40 is
  about a different part of the file than a message about the whole of it; uploaded files by their
  `AssetId`, which is what actually holds their bytes; a review by path and kind. A target named in
  three messages is one row with three mentions, not three rows — the person referred to one thing.
- **What it shows.**
  - **Headline.** Number of distinct references in the scope, and how many messages carried at
    least one. Then a legend with counts per kind — files, directories, images, diffs, skills,
    chats, projects, reviews — each with its icon, and only the kinds the scope actually has.
  - **Rows.** Most mentioned first, then by kind and path. Each row is the same label and tooltip
    the transcript draws for that reference, through `IResourcePresenter` (`Label`, `Hint`,
    `Summary`), so a reference reads identically in the widget and in the feed. A `@diff` row
    shows its captured totals (`+120 −34` or "no changes"). Six rows are shown, with "Show N more".
  - **A click** on a row asks `Home` to scroll the transcript to the earliest message in scope
    that named the target (`OnSelectMessage` → `ScrollToTurnAsync`), the same path Timeline rows use.
  - **Footer.** "In X of Y turns references were named".
- **Honesty about numbers.**
  - Every figure is a count of stored references; none of it is inferred from timestamps, so
    nothing carries "≈".
  - Mentions beyond the first are a count of messages, not of how many times the reference was
    actually sent to a model: a message that was never answered still counts.
  - References inside subtask transcripts are not in this branch's messages and are not counted.
- **Empty state.** "Files, directories, images, uploads, skills, diffs and chats the conversation
  refers to appear here."; in the last-turn scope with data elsewhere, "The last turn named no
  references."
- **Folded summary.** `N references · M messages`; null when nothing was named.
- **Data.** The visible branch's `ChatMessageView.Resources`; labels and tooltips by
  `IResourcePresenter`. No scope switch state is stored, and no provider figure is involved.
- **Architecture.** `IChatReferenceStatisticsCalculator` in `src/AI.Web/Widgets` returns a
  `ChatReferenceStatistics` record holding one `ChatReferenceEntry` per distinct target; a pure
  function over the branch with no DI. Bound in `Composition.cs` and tested in
  `tests/AI.Web.Tests/Widgets`. The widget is a `ChatWidget` with `Summary`, the scope switch and
  the shared `chat-usage-*` building blocks (`chat-usage-section`, `chat-usage-legend`,
  `chat-usage-empty`, `chat-usage-footnote`, `chat-usage-link`); the rows use `chat-references-*`.

## Models widget

Which models answered the chat (`ChatModelsWidget`, `IChatModelStatisticsCalculator`).

- **Scope.** Whole chat or last turn, with the same switch as the other widgets. The turn in the
  last-turn scope is chosen with the same rule Performance and Subtasks use (`ChooseTurn`), so the
  three widgets agree on which turn that is; the headline says "so far" while it runs.
- **Source of truth.** `TurnTokenUsage.AnswerModels` — one entry per answer request, as the Host
  recorded it, interrupted streams included. A model that only drafted a chat title, judged a tool
  risk, routed a skill or wrote a summary is not an answering model and does not appear. The names
  are quoted exactly as the endpoint reported them, so two spellings of one model stay two rows.
- **What it shows.**
  - **Headline.** Number of distinct answering models, and the scope's answer requests on the right.
  - **Rows.** Most requests first, then by name. Each row: the model name, a bar with its share of
    the scope's answer requests, that share as a percentage and its request count. The tooltip
    names the request count, the share and when its first and latest request were recorded.
  - **Footer.** "In X of Y turns models answered", and a note that tokens are not recorded per
    model.
- **What is *not* implemented, and why.** The per-model **token** share does not exist client-side.
  The chat ledger groups tokens by purpose (`ChatTokenUsage.ByPurpose`), not by model. The Host's
  `TokenUsageReport.ByModel` does group usage by model, but no client endpoint serves it, so the
  widget has nothing to read. Rather than divide the scope's tokens among models by request count — which
  would be a guess dressed as a figure — the widget shows request counts and request share and says
  in the footer that tokens are not recorded per model. If a per-model usage endpoint is added to
  the Host later, the calculator gains a field and the widget a row; the contract stays the same.
  Likewise nothing is inferred from message timestamps: how long a model took to answer is not in
  these records, and the widget does not invent it.
- **Empty state.** "Models that answer the chat appear here once it sends its first request."; in
  the last-turn scope with data elsewhere, "The last turn has no recorded answer yet."
- **Folded summary.** One model: `model · N`; several: `N · top-model P%`.
- **Data.** `ChatTokenUsage` (whole chat) and `TurnTokenUsage` (the run snapshot's live turn),
  the visible branch for the turn count, and the running flag. No presentation service is injected:
  the only formatted figure is a percentage, which the widget rounds itself.
- **Architecture.** `IChatModelStatisticsCalculator` in `src/AI.Web/Widgets` returns a
  `ChatModelStatistics` record holding one `ChatModelEntry` per model; a pure function over the
  usage pair and the branch. Bound in `Composition.cs` and tested in `tests/AI.Web.Tests/Widgets`.
  The widget is a `ChatWidget` with `Summary`, `ChatWidgetScopeSwitch`, the shared count headline
  and `chat-usage-*` building blocks; the rows use `chat-models-*` and reuse
  `chat-usage-share-track` for the bar.

## Team widget

Which branches are working with this one (`ChatTeamWidget`, `IChatTeamStatisticsCalculator`).

### The team as a whole

In a chat with teammates (branches with a `Member` identity, see
`docs/34-asides-and-team-messages.md`) the widget opens with the whole team, built by
`IChatTeamRosterCalculator` from every branch of the chat and the chat's run snapshots. It is the
same whichever branch is open, so a teammate's branch shows the task it is part of:

- **Task.** The person's first message on the main branch, up to three lines; a click opens the
  lead's branch at it. Under it: "X of N done", "K waiting for the lead" when teammates have an
  unanswered question or blocker, and a **Charter** link to the lead's latest "Team charter".
- **Roster.** The lead, then every teammate in the order they were started, each with its colour
  dot and "Name · Role" ("· this branch" on the visible one), its state — Working, Needs you (a
  confirmation or question waits), Stopped (failed, interrupted or paused), Done (latest report is
  `done`), Waiting — and its latest report to the lead, intent and first line. A teammate whose
  latest question or blocker came after the lead's last message into its branch shows
  "Waiting for the lead: …". A click opens that member's branch.
- **Summary.** "X/N done", with "· K waiting" when there are open items.

The statistics below, under "Messages into this branch", are the per-branch figures described next.

- **Scope.** Whole chat or last turn, with the same switch as the other widgets. The visible branch
  is split into turns by the same rule the Models and Branches widgets use, so "the last turn" means
  the same thing in all of them; the headline says "so far" while it runs.
- **Source of truth.** `ChatMessageView.Sender` — the `{ ChatId, BranchId, Intent }` the Host records
  beside a message from the sending run's own context (see `docs/34-asides-and-team-messages.md`).
  A message without a sender is the person's or this branch's own and is not counted. Nothing is
  inferred from message text, from the branch list or from `Sender.ChatId`: a message naming another
  chat is still a message into this branch and is counted as one.
- **What it shows.**
  - **Headline.** Number of participating branches, and the scope's team messages on the right.
  - **Legend.** Intent totals for the scope, in the protocol's order
    (`question`, `answer`, `decision`, `blocker`, `status`, `done`), then any the protocol does not
    define, in their own spelling.
  - **Rows.** Most messages first, then by title. Each row: the branch title, its message count, how
    many of those were asides, its intent counts and the arrival time of its latest message in
    scope. A click shows that message in the transcript.
  - **Footer.** "In X of Y turns team messages arrived", and a note that every figure comes from the
    recorded sender and that arrival times are message timestamps.
- **Honesty rules.**
  - **Only branches that sent something appear.** The widget reports what happened, not who was
    invited: a teammate that has sent no message yet is not listed, and work a branch did without
    reporting it is invisible here.
  - **Arrival times are approximate and marked "≈".** They come from `ChatMessageView.CreatedAt`,
    which is when the Host stored the message, not when the sending branch wrote it. The tooltip
    says so, and so does the footer note.
  - **Intent words come from the sender**, quoted as the sending run named them. An intent this
    build does not know keeps its own spelling instead of being folded into a bucket, and the
    tooltip says the build has no wording of its own for it.
  - **No cost or token data.** The widget deliberately states nothing about what the team spent.
- **Empty state.** "Branches that send team messages into this one appear here."; in the last-turn
  scope with data elsewhere, "The last turn exchanged no team messages."
- **Folded summary.** `N branch/branches · M`; null when no branch took part.
- **Data.** The visible branch and the chat's stored branches, the second only to name a sending
  branch — a branch the chat no longer stores is still listed, under the `Branch` fallback the
  Branches widget uses. No presentation service is injected.
- **Architecture.** `IChatTeamStatisticsCalculator` in `src/AI.Web/Widgets` returns a
  `ChatTeamStatistics` record holding one `ChatTeamBranchSummary` per branch, each with its
  `ChatTeamIntentCount` list; a pure function over the branch. Bound in `Composition.cs` and tested
  in `tests/AI.Web.Tests/Widgets`. The widget is a `ChatWidget` with `Summary`,
  `ChatWidgetScopeSwitch`, the shared count headline and `chat-usage-*` building blocks; the rows
  use `chat-team-*`. A blocker wears the warning status token, and its word stays in the text, so
  colour never carries the meaning alone.

## Unfinished work widget

Which work in this project has not finished and still wants the person
(`ChatUnfinishedWidget`, `IChatUnfinishedStatisticsCalculator`). It is the widget to open first after
starting the application, when runs left over from the last session are waiting.

- **Scope.** The whole project, not the open chat, so it has no whole-chat/last-turn switch. The
  widget sits in whichever chat is open, but its list is the same everywhere: every run snapshot the
  client holds for the project's chats. The open chat's rows are marked instead.
- **Source of truth.** `IRunStateService.Runs` — the run snapshots the client itself keeps, loaded at
  startup and kept in step by the Host's pushes. Each one carries `Status`, `Queue`, `PendingApproval`,
  `PendingPrompt`, `Wait`, `Error` and `RecoveryActions`; the chat's own title and last-activity time
  come from the sidebar's `ChatSummary`. Nothing is inferred from token usage.
- **What counts as unfinished.** A run is listed while it is `Generating`, `Paused`, `Interrupted` or
  `Failed` with a recovery action left, while it has a pending approval, a pending question or a
  provider wait, or while messages are still queued behind it.
- **What it shows.**
  - **Headline.** The total number of unfinished runs and how many chats they are in; each branch of a
    chat counts as its own run. On the right, how many of them need the person (in the warning colour),
    or "nothing needs you" when every run is still moving on its own.
  - **Legend and filter.** One entry per reason the project actually has, most demanding first:
    `Approval`, `Prompt`, `Wait`, `Failed`, `Paused`, `Interrupted`, `Running`, `Queue`. With more than
    one reason present each entry is a toggle button (`aria-pressed`) that narrows the list to that
    reason; pressing it again shows everything. A filter whose reason has gone from the project stops
    narrowing on its own.
  - **Actions.** `Resume all (N)` — `Resume shown (N)` while filtered — resumes every listed run that
    waits for a resume, one after another. While it works the button reads `Resuming i of N…` and every
    resume button is disabled. The note beside it says how many listed runs are handled in their chat.
  - **Rows.** Most demanding first, then by chat title; the first six, with `Show N more` / `Show fewer`
    beneath a longer list. Each row: the chat title; a detail line saying what the run waits on when the
    snapshot says it — `Approve <tool>` (with `i of n` for a batch), `Asks: <first question>` (with
    `+n more`), `Tries again at <time>`, the failure message, or `Next: <queued message>`, cut to one line
    of 120 characters; then the reason, `open now` or `this chat` for the open chat's runs (the row also
    gets `aria-current` and the accent background when it is the open branch), whether the run works on
    a branch, how many messages are queued, the run's own status only when it says more than the reason,
    and the chat's last-activity time. Times are compact: `HH:mm` today, `yesterday HH:mm`,
    `d MMM HH:mm` this year, the date otherwise; the tooltip has the full time. The row opens the chat —
    and its branch, when the run works on a fork. Rows waiting for a resume carry their own resume
    button, which turns into a spinner and is disabled while its request is in flight, so a second click
    cannot send it twice.
  - **Footer.** "from run snapshots" and the note that last-activity times are message timestamps.
- **Honesty rules.**
  - **Only runs the client knows about.** The list is this client's own run snapshots, not a query of
    the Host. A run the Host no longer keeps is not listed; a run the client has not yet received at
    startup appears when it arrives.
  - **Last-activity times are approximate and marked "≈".** They come from the chat's stored summary,
    which is when something was written to the chat — not when the run stopped — and they include time
    the chat sat idle. The tooltip and the footer note both say so. A retry time is the Host's own and
    carries no "≈".
  - **Resume is only offered where it is what the run waits for**: paused, interrupted, idle with
    messages queued, or failed with `Resume` among its recovery actions. An approval or a question needs
    an answer, a limit lifts on its own and a generating run is already moving — even with messages
    queued behind it — so those rows open their chat instead of showing a button that would be refused
    or do nothing. A failure without `Resume` is not offered one even when messages are queued.
  - **Empty chats and archived chats are left out**, even when a stale snapshot still names them.
- **Empty state.** "Nothing is unfinished. Runs that wait for you, were paused or interrupted, failed
  or still have messages queued appear here."
- **Folded summary.** `N need/needs you · M unfinished` while anything needs the person, otherwise
  `M unfinished · K chat/chats`; null when nothing is unfinished.
- **Data.** The sidebar's `ChatSummary` list and `IRunStateService.Runs`, plus the open chat and branch
  from the page. No presentation service is injected; the page resumes one run per `OnResume` call and
  the widget sequences "Resume all" itself so it can show progress.
- **Architecture.** `IChatUnfinishedStatisticsCalculator` in `src/AI.Web/Widgets` returns a
  `ChatUnfinishedStatistics` record holding one `ChatUnfinishedTask` per unfinished run; a pure
  function over the chats and the run snapshots. Bound in `Composition.cs` and tested in
  `tests/AI.Web.Tests/Widgets`. The widget is a `ChatWidget` with `Summary`, the shared count headline
  and `chat-usage-*` building blocks; the rows use `chat-unfinished-*`. Only a reason that asks a
  person to act wears a status word colour — `--color-warning-text` for what blocks the run now,
  `--color-danger-text` for what stopped — and the reason word stays in the text beside the mark, so
  colour never carries the meaning alone.
