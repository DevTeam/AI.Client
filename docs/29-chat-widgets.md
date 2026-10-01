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
  of folded widgets reads as a status bar.
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
  summary — follows the choice. The choice is the widget's own and starts at "Whole chat".
- **The turn in progress.** A running turn counts as the last turn, and its figures grow as it
  goes. The widget says that they are not final ("so far", "This turn is still running").
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
| 1.35rem, 500 | One headline count (Files: number of files) |
| .86–.9rem, 500 | Headline figures (Usage: `154k → 7k` and cost; Files: `+307 −76`) |
| .78rem | Body text and rows |
| .74–.76rem | Grids, list figures, the scope switch, inline links |
| .7–.72rem | Legends, column headers, footnotes, `small` qualifiers |

Figures always use tabular digits (`font-variant-numeric: tabular-nums`) and do not wrap.

### Building blocks

| Block | Classes | Looks like |
| --- | --- | --- |
| Scope switch | `chat-widget-scope` (component `ChatWidgetScopeSwitch`) | Two equal segments on `--color-fill`; the chosen one raised on `--color-surface` with a hairline ring |
| Heading row | `chat-usage-row chat-usage-heading` | Label left, figure right, baseline aligned |
| Headline | `chat-usage-row chat-usage-headline` | The scope's main figures, .9rem 500, qualifiers in `small` |
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
made; they do not repeat it.

## Architecture

### Pieces

| Piece | File | Role |
| --- | --- | --- |
| `ChatWidgetDefinition` | `src/AI.Web/Widgets/ChatWidgets.cs` | Id, title, icon, description of a kind of widget |
| `IChatWidgetCatalog` | same | Every widget this build has, in the order a new column shows them |
| `ChatWidgetPreference` | same | One widget as the person left it: `Id`, `Hidden`, `Collapsed` |
| `IChatWidgetLayout` | same | `Arrange` saved preferences against the catalog; `Move`, `MoveBy`, `Update` |
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
  remain separate. The footer shows turns with calls and whether the current turn is running.
- **Folded summary.** Call count and errors for the selected scope.
- **Live data.** The headline says so far during a running or paused turn. Draft calls with
  arguments still streaming are not counted until recorded or executing. No durations are inferred
  from message timestamps, which do not measure tool execution time.
