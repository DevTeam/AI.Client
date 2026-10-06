# Project development rules

When adding or changing services and composition consumers, follow [the Pure.DI conventions](docs/30-dependency-injection.md). Prefer interface dependencies, lifetime-specific bindings, transient lifetimes unless shared state requires a singleton, and only the roots needed by actual consumers. Use `nameof` for named roots. Keep generation comments disabled and disable composition thread safety only when its access is known to be serialized.

Before changing any web UI style (`src/AI.Web/wwwroot/css/app.css`, inline `style` attributes in Razor, styles set from `src/AI.Web/wwwroot/js`), read [Styles and palette](docs/33-styles-and-palette.md) and follow its Regulation section strictly. In short:

- Colours, shadows, scrims, corner radii, font families, font sizes, global `z-index` values and interaction durations come only from the design tokens defined at the top of `app.css`. No hex, `rgb()`, named colours or one-off values in component rules, not even once; the only exceptions are the ones listed in the regulation.
- Choose a token by its role (the regulation's tables), not by the value that looks closest. Status words use the `-text` status tokens.
- Do not add `*.razor.css`, `<style>` blocks or stylesheets, and do not set colour, font, shadow, radius or `z-index` from inline styles or scripts.
- A new token is added only when no existing role fits, for every theme family at once, and is documented in the same change.
- Check a styling change in a dark and a light theme and with a non-blue accent.

Source comments, identifiers, UI text, technical messages, and project documentation are written in English.
