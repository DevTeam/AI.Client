# Styles and palette

Status: Accepted, mandatory. Applies to every change to the web UI: `src/AI.Web/wwwroot/css/app.css` (the only stylesheet; scoped `*.razor.css` files are not loaded), inline `style` attributes in Razor components, and styles set from `src/AI.Web/wwwroot/js`.

## Regulation

These rules are binding for every contributor, human or model. A change that breaks one of them is incomplete, whatever else it achieves.

### Must

1. Every value of the properties below is a token from the [token layers](#token-layers), a `color-mix()` of tokens, or one of the listed exceptions:

   | Property | Allowed value |
   | --- | --- |
   | `color`, `background`, `background-color`, `border-color`, `outline-color`, `fill`, `stroke`, `caret-color`, colours inside `border`, `outline`, gradients | `var(--color-*)`; `transparent`, `currentColor`, `inherit` |
   | `box-shadow`, `filter: drop-shadow()` | `var(--shadow-*)`, optionally followed by a ring built from tokens (`0 0 0 2px var(--color-accent)`) |
   | Backdrop and scrim backgrounds | `var(--color-scrim*)` |
   | `border-radius` | `var(--radius-*)`, `var(--chat-card-*-radius)`, `50%` for a circle, `inherit`, `0`; `1px`–`2px` only for elements smaller than `.6rem` (hairlines, tracks, legend swatches) |
   | `font-family` | `var(--font-sans)`, `var(--font-mono)`, `inherit` |
   | `font-size` (also inside the `font` shorthand) | `var(--font-size-*)`, `var(--message-font-size)`, `em` relative to the parent, `inherit` |
   | `z-index` of anything positioned against the viewport or floating over other components | `var(--z-*)` |
   | `transition` durations and easings of hover, press and state changes | `var(--duration-*)`, `var(--ease-emphasized)`, keywords `ease`, `ease-out`, `ease-in`, `linear` |

2. Pick the token by role, using the tables below ([colour](#choosing-a-colour), [shadow](#choosing-a-shadow), [layer](#stacking-layers), [motion](#motion)), not by the value that looks closest.
3. Status words use the `-text` status tokens; the bare `--color-danger`, `--color-warning`, `--color-success` are for icons, dots and bars.
4. A selector or script that means "a light theme" covers both light themes (see [Themes](#themes)).
5. A new token is added only when no existing role fits. It gets a role comment, it is defined for the dark base and the light family at once (and for darkblue, gray and lightgray when they differ), and it is added to the tables in this document in the same change.
6. A change to `--color-bg` of a theme updates `chromeColors` in `js/theme.js` and `WindowBackgroundBrush` in `AI.Desktop/App.axaml`.

### Must not

- Write a hex, `rgb()`, `hsl()` or named colour (`white`, `black`, …) outside the token blocks at the top of `app.css`.
- Write a new shadow, radius, font size, font stack, global `z-index` or interaction duration as a literal, even once, even "close to" an existing token.
- Add a scoped `*.razor.css` file, a `<style>` block, or a second stylesheet.
- Set colour, font, shadow, radius or `z-index` from an inline `style` attribute or from JavaScript. Inline styles and scripts set only runtime geometry (`width`, `height`, `left`, `top`, `transform`, `flex-grow`) and component custom properties (`--count`, `--swatch`, `--branch-depth`, …).
- Change a value inside a theme block to fix one component. Fix the component's choice of token instead.
- Copy a token value into a component rule to tweak it; derive with `color-mix()` from the token.

### Exceptions

Only these places may keep literal values:

- The token blocks at the top of `app.css`.
- Self-contained illustrations whose colours are part of the drawing: the guide's ghost cursor (`.ghost-cursor*`).
- `z-index` 1–25 for stacking inside one component or inside a component's own stacking context.
- Durations of choreographed animations that scripts wait for: the guide, the ghost cursor, sidebar and drawer entry and exit, pulses and spinners (see [Motion](#motion)).
- The nudge of a blocked send (`.composer-setup-hint.is-nudged`): a short shake that is a gesture, not a state change, and is off under reduced motion.
- Durations of indicators whose motion follows data: the context ring, animated counts, progress bars and the countdowns of timed prompts and guide steps.
- `em` font sizes inside markdown and code, and the `clamp()` headings of the host gate and hero.

A new exception is added to this list in the same change, with its reason.

### Before finishing a change

- Search the changed lines for `#`, `rgb(`, `hsl(`, `px` shadows, `font-size:`, `border-radius:`, `z-index:`, `ms`/`s` durations and `font-family:`; each match is a token or a listed exception.
- Check the change in a dark and a light theme, and with a non-blue accent.
- If a token was added, it is in this document.

## Token layers

| Layer | Tokens | Where defined |
| --- | --- | --- |
| Fonts | `--font-sans`, `--font-mono` | First `:root` block |
| Font sizes | `--font-size-2xs` (.65rem), `-xs` (.7rem), `-sm` (.75rem), `-md` (.8rem), `-base` (.85rem), `-lg` (.9rem), `-xl` (1rem), `-2xl` (1.15rem), `-3xl` (1.35rem); `--message-font-size` for message text | First `:root` block. Sizes in `em` (relative to the parent) and the hero's `clamp()` headings stay as written. |
| Corner radii | `--radius-2xs` (.2rem), `-xs` (.3rem), `-sm` (.4rem), `-md` (.5rem), `-lg` (.65rem), `-xl` (.85rem), `-2xl` (1rem), `-3xl` (1.5rem), `--radius-pill` | First `:root` block; all but the pill are multiplied by `--corner-scale` (Settings → Corner roundness). A circle stays `50%`. |
| Palette | `--color-*` | Theme blocks |
| Accent swatches | `--accent-swatch-*` | Accent block; also the colours the Settings picker paints and the hues of the context-window layers |
| Elevation | `--shadow-xs`, `-sm`, `-md`, `-lg`, `-xl`, `--shadow-drawer-start`, `--shadow-drawer-end` | Elevation block; scaled per theme by `--shadow-scale` |
| Backdrops | `--color-scrim`, `--color-scrim-soft`, `--color-scrim-faint` | Elevation block; scaled per theme by `--scrim-scale` |
| Stacking layers | `--z-*` | Stacking block |
| Motion | `--duration-fast` (120ms), `--duration-base` (160ms), `--duration-slow` (200ms), `--ease-emphasized` | Motion block |

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

## Stacking layers

Anything that floats above the page takes a `--z-*` layer, from the bottom up:

| Layer | Value | Used by |
| --- | --- | --- |
| `--z-menu-backdrop`, `--z-menu`, `--z-menu-raised` | 29, 30, 31 | Menus and their click-catching backdrops; `-raised` for a menu or tooltip opened over another menu |
| `--z-drawer-scrim`, `--z-drawer`, `--z-modal` | 39, 40, 40 | Phone sidebar, widget rail, ordinary modals |
| `--z-side-panel-backdrop`, `--z-side-panel` | 44, 45 | Permissions and archive drawers |
| `--z-modal-raised` | 46 | A modal opened from a side panel |
| `--z-floating` | 50 | Toasts and lists that must clear every drawer and modal |
| `--z-review`, `--z-review-popover` | 100, 110 | Review workspace and its comment editor |
| `--z-guide-trail` … `--z-guide-cursor`, `--z-drag`, `--z-tooltip` | 999–1003 | Application guide, drag ghost, tooltips |
| `--z-system` | 10000 | Restart notice |

A number is still right for stacking inside one component (a badge over its icon, a resizer over its panel); such values stay at 1–7, and a component that positions its own menu inside a stacking context (the widget rail, the branch picker) keeps its local values too.

## Motion

Hover, press and state transitions take `--duration-fast` for colour and opacity, `--duration-base` for small movement, `--duration-slow` for expanding and sliding panels; `--ease-emphasized` is for things that arrive. A `visibility 0s linear …` delay that hides an element after it slides away uses the same token as the slide. Longer choreography (the guide, the ghost cursor, drawer entry and exit, pulses and spinners) keeps its own timing, because scripts wait for it (`js/appGuide.js`, `js/navigationCue.js`, `js/drawerTransitions.js`).
