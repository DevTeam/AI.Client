---
id: settings-create-theme
name: Settings create theme
icon: palette
aliases: ["theme", "new-theme"]
kind: playbook
description: Build a new colour theme for the application window from a description, a mood, a brand colour or an attached image, preview it live and save it once the user agrees.
parameters: {"type":"object","properties":{"description":{"type":"string","description":"What the theme should feel like, in the user's words"}},"additionalProperties":false}
tools: ["app_theme","ask_user"]
---

A theme is four seeds, and the window derives every other colour from them:

- `background`: the page colour, `#rrggbb`. It sets the hue and the lightness of everything.
  Dark themes read best with a background at about 8–18% lightness, light themes at about
  92–98%; a mid-tone background cannot carry readable text in either family.
- `family`: `dark` or `light`. Leave it out unless the user asked for the other one; the
  background decides.
- `contrast`, 0–100, default 50: low is soft and calm, high is crisp. Text never drops below
  the readable minimum, whatever the dial.
- `saturation`, 0–100, default 50: how much of the background's hue reaches surfaces, borders
  and text. 0 is neutral grey; 70 and above is visibly tinted.

The accent (links, buttons, focus) is a separate choice of blue, teal, green, amber, orange,
pink, purple or indigo, given with Save. Status colours are not part of a theme.

1. Read the request: `description`, the user's message, or an attached image. For an image, take
   its dominant large-area colour as the background and its mood for the dials; for a brand
   colour, use it as the accent when it is one of the eight hues, and derive a dark or light
   background of the same hue rather than using a saturated brand colour as the page.
   If nothing says light or dark, call `app_theme` operation `get` and keep the family of the theme
   in use.
2. Choose the seeds and a short evocative name of one to three words. Call `app_theme` operation
   `preview` with `background`, `name` and any dial or family that differs from the defaults.
   Start from no `themeId`: the preview is a new theme.
3. Read the result. When `meetsReadableContrast` is false, move the background further from mid
   lightness or raise `contrast`, and preview again; do this at most three times, then say plainly
   which pair is still short. Compare `colors` with the request: if the user asked for warmth and
   `bg` is cool, adjust the hue before showing anything.
4. In one or two sentences say what is on the page now: the name, the feel, and the seeds that
   make it. Ask with one `ask_user` (answerStyle='buttons'): "Save it (Recommended)", "Adjust",
   "Discard". Add an accent suggestion to the Save label only when the request implied one.
   For "Adjust", ask what to change in free text, preview the change (name only the seeds that
   change; the preview continues from the last one) and ask again.
5. Save: `app_theme` operation `save` without `themeId`, with `accent` when one was agreed.
   Discard, or a dismissed, expired or declined question: `app_theme` operation `discard`.
   Answer in one line what the window now shows.

Never call `save` or `activate` without the user's agreement in this conversation.
