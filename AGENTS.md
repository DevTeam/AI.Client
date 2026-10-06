# Project development rules

When adding or changing services and composition consumers, follow [the Pure.DI conventions](docs/30-dependency-injection.md). Prefer interface dependencies, lifetime-specific bindings, transient lifetimes unless shared state requires a singleton, and only the roots needed by actual consumers. Use `nameof` for named roots. Keep generation comments disabled and disable composition thread safety only when its access is known to be serialized.

Web UI styles use the design tokens of `app.css` (colours, shadows, scrims, radii, fonts) instead of literal values, and a new token is defined for every theme family; see [Styles and palette](docs/33-styles-and-palette.md).

Source comments, identifiers, UI text, technical messages, and project documentation are written in English.
