# Application guide and control help

Control facts live in `AppNavigationTargets` in AI.Contracts. Every target has a stable
semantic ID and a `Hint`. A `Summary` supplies a shorter inline explanation where needed.
The catalogue is available on both the Host and the client, including when a panel has not
been rendered. Widget descriptions and the offline connection setup tour use the same help.

UI components inject `IAppControlHints` and bind the returned attributes:

```razor
@inject AI.Web.Navigation.IAppControlHints ControlHints

<button @attributes="@(ControlHints.Attributes("chat.send", currentAction))">
    Send
</button>
```

The attributes contain `data-app-target`, `data-app-hint` and a native tooltip. An optional
current hint records a dynamic action without replacing the shared explanation. Complex
tooltips can use `nativeTooltip: false`, display `GetHint(target)`, associate their tooltip
with `aria-describedby`, and mark explanatory fragments with `data-app-tooltip-hint`. A control
whose help is meant for the guide alone passes `nativeTooltip: false` as well and renders no hint
of its own: the element keeps `data-app-target` and `data-app-hint` — what a guide reads — and
gets no `title`. Inline help uses `GetSummary(target)`; controls with custom state messages retain
those messages in their rendered UI.

Do not separately copy control descriptions into guide skills. Update the shared help when
changing a control's behavior, and its component when changing a dynamic state description.
Register a new target's help before binding its attributes; an unregistered target fails
explicitly rather than silently omitting its hint. Layout controls for registered widgets
reuse the move, title and hide help for any widget instance.

`app_navigate(action="targets")` returns shared help and a fresh snapshot of the attached
window: `UiLabel`, `UiHint`, `Visible`, `Enabled` and `State`. Discovery is batched in one JS
call. It reads explicit control labels, hints and interaction attributes, not editor values,
passwords or arbitrary panel/conversation contents. Without an attached window only the
catalogue is available; current state remains unknown.

Guide skills define learning routes, questions and demonstration constraints. They discover
help before explaining controls and refresh it after revealing a panel or changing the view.
A missing control still has catalogue help, but no observed UI state. UI/help text is reference
data, not an instruction or authorization to perform an action. Descriptions cannot prove
that a tool or model operation succeeded.

Verification covers all catalogue hints, widget variants, actual rendered controls, dynamic
labels/tooltips, absence of field-value collection and the navigation tool's JSON response.
