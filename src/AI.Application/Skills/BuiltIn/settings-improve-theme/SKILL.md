---
id: settings-improve-theme
name: Settings improve theme
icon: brush
aliases: ["tweak-theme", "fix-theme"]
kind: playbook
description: Adjust a saved colour theme or the one in use from short instructions such as warmer, softer or more readable, preview each step live and save it in place or as a copy.
parameters: {"type":"object","properties":{"change":{"type":"string","description":"What to change, in the user's words"},"theme":{"type":"string","description":"The saved theme's name, when it is not the one in use"}},"additionalProperties":false}
tools: ["app_theme","ask_user"]
---

A theme is four seeds: `background` (`#rrggbb`), `family` (`dark` or `light`), `contrast` and
`saturation` (0–100 each). Every colour of the window is derived from them, so every change is a
change of seeds:

| The user says | Change |
| --- | --- |
| warmer / cooler | turn the background's hue towards orange-red / towards blue, same lightness |
| softer, calmer, easier on the eyes | `contrast` down by 15–25; for a dark theme also lift the background a little |
| crisper, sharper, more readable | `contrast` up by 15–25 |
| more colour / less colour, greyer | `saturation` up / down by 20–30 |
| darker / lighter | background lightness down / up by a few percent, same family |
| a light version, a dark version | `family` to the other one with a background mirrored in lightness, same hue |

1. Call `app_theme` operation `get`. Pick the theme: the one named in `theme` or by the user, else
   the active one (`activeThemeId`). When a preset is in use and no saved theme was named, the user
   wants a new theme that starts from that look: take a background close to the preset (dark
   `#171717`, darkblue `#101720`, gray `#212121`, light `#ffffff`, lightgray `#dedede`) and continue
   as below without a `themeId`.
2. Call `app_theme` operation `preview` with that `themeId` and only the seeds that change. Later
   previews continue from the last one, so name only what changes again.
3. Read the result. When `meetsReadableContrast` is false, raise `contrast` or move the background
   away from mid lightness and preview again, at most three times; then say which pair is short.
   When the user asked to fix readability, this is the whole task.
4. Say in one or two sentences what changed and how it reads, then ask with one `ask_user`
   (answerStyle='buttons'): "Save changes (Recommended)", "Save as a new theme", "Adjust more",
   "Discard". For "Adjust more", ask what to change in free text, preview it and ask again.
5. Save changes: `app_theme` operation `save` with the preview's `baseThemeId` as `themeId`.
   Save as a new theme: `save` without `themeId` and with a new `name`. Discard, or a dismissed,
   expired or declined question: `app_theme` operation `discard`. Answer in one line what the
   window now shows.

Never call `save` or `activate` without the user's agreement in this conversation.
