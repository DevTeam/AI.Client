# Styles and palette

Status: Accepted. Applies to `src/AI.Web/wwwroot/css/app.css`, the only stylesheet of the web UI (scoped `*.razor.css` files are not loaded).

## Rule

A component rule uses design tokens, never literal values, for colour, shadow, backdrop, corner radius and font family. A new visual need is solved by an existing token first; a new token is added only when no existing role fits, and it is added for every theme family at once. Literal colours are allowed only inside the token blocks at the top of `app.css` and in self-contained illustrations whose colours are part of the drawing (the guide's ghost cursor).

## Token layers

| Layer | Tokens | Where defined |
| --- | --- | --- |
| Fonts | `--font-sans`, `--font-mono` | First `:root` block |
| Corner radii | `--radius-2xs` (.2rem), `-xs` (.3rem), `-sm` (.4rem), `-md` (.5rem), `-lg` (.65rem), `-xl` (.85rem), `-2xl` (1rem), `-3xl` (1.5rem), `--radius-pill` | First `:root` block; all but the pill are multiplied by `--corner-scale` (Settings → Corner roundness). A circle stays `50%`. |
| Palette | `--color-*` | Theme blocks |
| Accent swatches | `--accent-swatch-*` | Accent block; also the colours the Settings picker paints and the hues of the context-window layers |
| Elevation | `--shadow-xs`, `-sm`, `-md`, `-lg`, `-xl`, `--shadow-drawer-start`, `--shadow-drawer-end` | Elevation block; scaled per theme by `--shadow-scale` |
| Backdrops | `--color-scrim`, `--color-scrim-soft`, `--color-scrim-faint` | Elevation block; scaled per theme by `--scrim-scale` |

## Themes

The dark theme is the complete base on `:root`; every other theme inherits from it and declares only what it changes:

```
dark (base) ─┬─ darkblue    surfaces, fills, text and borders shifted towards blue
             ├─ gray        surfaces, fills and borders lifted to neutral grey
             └─ light family (light, lightgray)
                  └─ lightgray   surfaces, fills and borders a hair darker
```

A selector that means "a light theme" names both light themes: `:root:is([data-theme="light"], [data-theme="lightgray"])`. Script that needs the same answer reads the computed `color-scheme` of `<html>` instead of comparing theme names (see the Mermaid configuration in `index.html`).

Accent and status colours belong to the accent the user picked. They are written once per family, so switching between themes of one family never recolours them. Blue is hand-tuned in the theme blocks; every other accent derives all `--color-accent-*` tokens from its swatch, once for the dark family and once for the light family.

The browser chrome colour in `js/theme.js` and the window background in `AI.Desktop/App.axaml` mirror `--color-bg` of each theme and change together with it.

## Choosing a colour

| Need | Token |
| --- | --- |
| Page, panels, cards | `--color-bg-sunken` < `--color-bg-inset` < `--color-bg` < `--color-surface` < `--color-surface-raised` |
| Text fields | `--color-input-bg` |
| Rows, chips, buttons at rest / hovered / pressed | `--color-fill`, `--color-fill-hover`, `--color-fill-active`; `-strong`, `-stronger` for emphasis |
| An element over more than one surface | `--color-overlay-*` |
| Text by importance | `--color-text-strong` > `--color-text` > `-secondary` > `-muted` > `-subtle` > `-faint`; `-disabled` only for placeholders and disabled controls |
| Borders | `--color-border-subtle` < `--color-border` < `-strong` < `-stronger`; `-translucent` over images and mixed surfaces |
| Links, interactive icons, focus outlines | `--color-accent` |
| Accent fills, rings, progress | `--color-accent-strong` |
| Softer accent text, words on accent tints | `--color-accent-text` on `--color-accent-bg*` |
| Solid accent block | `--color-accent-fill` under `--color-on-accent` |
| Status icons and dots | `--color-danger`, `--color-warning`, `--color-success` |
| Status words | `--color-danger-text`, `--color-warning-text`, `--color-success-text` |
| Status tints and borders | `--color-danger-bg*`, `--color-danger-border*`, `--color-success-border` |
| Send button | `--color-inverse*` under `--color-on-inverse` |

A one-off tint is `color-mix(in srgb, var(--color-…) N%, transparent)` over an existing token rather than a new literal.

## Choosing a shadow

| Element | Token |
| --- | --- |
| Switch thumb, selected segment | `--shadow-xs` |
| Tooltip, floating button, drag ghost | `--shadow-sm` |
| Menu, popover, dropdown list | `--shadow-md` |
| Toast, guide step, card that floats on its own | `--shadow-lg` |
| Modal dialog | `--shadow-xl` |
| Drawer from the start / end edge | `--shadow-drawer-start` / `--shadow-drawer-end` |

A ring (focus, selection, a 1px hairline) is not elevation and is written where it is used; it can be combined with an elevation token: `box-shadow: var(--shadow-lg), 0 0 0 1px …`.

Backdrops: `--color-scrim` behind a modal, `--color-scrim-soft` behind the phone drawer, `--color-scrim-faint` behind side drawers.

## Known follow-ups

- Font sizes use about forty distinct values between `.56rem` and `1.4rem`, mostly a step of `.02rem` apart. They should be reduced to a type scale of six to eight tokens; that changes the look of many components and is a separate pass.
- `z-index` values are local per component (from `1` to `10000`). Global layers (sticky chrome, menus, drawers, modals, guide, toasts) should become a small set of tokens.
- Transition timings (`120ms`, `140ms`, `150ms`, `160ms`, `180ms`, `200ms` with various easings) should become two or three duration and easing tokens.
